using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Configuration;
using HarmonyLib;
using OS.Items;
using UnityEngine;

namespace CornerShopsMod
{
	/// <summary>
	/// Corner Shops: the hotel and motel keepers (Arvid Vinter, Carl Hagen at Hotel Korso, Joran Kallio at the
	/// Pickaxe Motel) sell everyday supplies: food, drinks and utility items. Look at one and press Shift+E to open
	/// the game's own trade screen with their stock; plain E still talks. Stock refills every in-game day and is kept in your save.
	/// </summary>
	[BepInPlugin(Guid, "Obenseuer Corner Shops", Version)]
	public class CornerShopsPlugin : BaseUnityPlugin
	{
		public const string Guid = "com.juanberta.obenseuer.cornershops";
		public const string Version = "0.2.0";

		internal static ManualLogSource Log;
		internal static CornerShopsPlugin Instance;

		private ConfigEntry<string> vendors, stock;
		private ConfigEntry<KeyboardShortcut> shopKey;
		private ConfigEntry<float> buyFactor, sellFactor;
		private ConfigEntry<bool> buysFromPlayer;
		private ConfigEntry<int> traderMoney;

		private readonly Dictionary<string, Shop> shops = new Dictionary<string, Shop>(StringComparer.OrdinalIgnoreCase);
		private int openedFrame = -1;

		private const string DefaultStock =
			// food
			"Rye Bread:5, White Bread:5, Multigrain Bread:3, Canned Meat:4, Canned Tuna:4, Canned Sardines:4, Can of Pea Soup:4, " +
			"Can of Tomato Soup:4, Canned Peas:3, Can of Peaches:3, Lenkki Sausages:4, Lies Potato Chips:5, Carl Fatler Chocolate Bar:5, " +
			"Pluto Candy Bar:5, " +
			// drinks
			"Moca-Cola Can:6, Splurt Soda:6, Fail Soda:6, Ice Tea:4, Milk:3, " +
			// utility
			"Can Opener:2, Matchbox:5, Lighter:2, Tiny Candle:5, Batteries AA:4, Batteries D:4, Toilet Paper:6, Bar Soap:3, " +
			"Bandage:3, Painkillers:2, Duct Tape:2, Glue:2, " +
			// paperwork
			"Paper:10, Notebook:3, Empty Envelope:6, Stamps:6, Bottle of Ink:2, Fancy Paper:4";

		private class Shop
		{
			public string Name;
			public GameObject Root;
			public ShopShelf Shelf;
			public ShopTrade Till;
		}

		private void Awake()
		{
			Log = Logger;
			Instance = this;
			vendors = Config.Bind("General", "Vendors", "Arvid Vinter, Carl Hagen, Joran Kallio",
				"Comma-separated names of the NPCs who run a shop (as shown when you look at them).");
			shopKey = Config.Bind("General", "ShopKey", new KeyboardShortcut(KeyCode.E, KeyCode.LeftShift),
				"Opens the shop of the vendor you are looking at. Talking (the game's interact key alone) is unchanged.");
			stock = Config.Bind("Stock", "Items", DefaultStock,
				"What every vendor sells: comma-separated 'Item name:amount' (item names as the game shows them). Refilled every in-game day.");
			buyFactor = Config.Bind("Prices", "BuyPriceFactor", 1.25f,
				new ConfigDescription("Price you pay, as a multiple of the item's value (the game's traders use 1.2).", new AcceptableValueRange<float>(0.5f, 3f)));
			sellFactor = Config.Bind("Prices", "SellPriceFactor", 0.6f,
				new ConfigDescription("Price they pay when you sell to them, as a multiple of the item's value.", new AcceptableValueRange<float>(0.1f, 1.5f)));
			buysFromPlayer = Config.Bind("Prices", "BuysFromPlayer", true, "Vendors also buy things from you (like the game's traders).");
			traderMoney = Config.Bind("Prices", "VendorCash", 2000, new ConfigDescription("Cash (OC) a vendor has each day to buy from you.", new AcceptableValueRange<int>(0, 100000)));

			var harmony = new Harmony(Guid);
			MethodInfo saveLoad = AccessTools.Method(typeof(SaveController), "ExecuteDontDestroyOnLoadSaveLoadFunctions");
			if (saveLoad != null) harmony.Patch(saveLoad, prefix: new HarmonyMethod(typeof(CornerShopsPlugin), nameof(BeforeSaveLoad)));
			else Logger.LogWarning("Couldn't find the save hook: the shops' stock won't be saved.");
			harmony.Patch(AccessTools.Method(typeof(InteractObjects), "UseInteractable"),
				prefix: new HarmonyMethod(typeof(CornerShopsPlugin), nameof(UsePrefix)));
			harmony.Patch(AccessTools.Method(typeof(InteractableTalk), "UpdateUseMessage"),
				postfix: new HarmonyMethod(typeof(CornerShopsPlugin), nameof(UseMessagePostfix)));
			gameObject.AddComponent<ShopSortButton>();
			Logger.LogInfo("Obenseuer Corner Shops " + Version + " loaded. Vendors: " + vendors.Value);
		}

		// ---- Finding a vendor ----

		private HashSet<string> vendorNames;
		private string vendorsRead;

		private bool IsVendorName(string name)
		{
			if (vendorsRead != vendors.Value)
			{
				vendorsRead = vendors.Value;
				vendorNames = new HashSet<string>(vendors.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
			}
			return !string.IsNullOrEmpty(name) && vendorNames.Contains(name.Trim());
		}

		internal string VendorOf(InteractableTalk talk)
		{
			if (talk == null) return null;
			if (IsVendorName(talk.title)) return talk.title.Trim();
			try
			{
				NPCInfo info = talk.GetComponentInParent<NPCInfo>();
				string full = info != null && info.character != null ? info.character.FullName : null;
				if (IsVendorName(full)) return full.Trim();
			}
			catch (Exception) { }
			return null;
		}

		private InteractableTalk Looked()
		{
			var io = InteractObjects.instance;
			return io != null ? io.interactable as InteractableTalk : null;
		}

		private bool ModifiersHeld()
		{
			KeyboardShortcut k = shopKey.Value;
			foreach (KeyCode m in k.Modifiers) if (!Input.GetKey(m)) return false;
			return true;
		}

		// ---- Input ----

		private void Update()
		{
			if (Time.frameCount == openedFrame) return;
			if (!shopKey.Value.IsDown()) return;
			TryOpen(Looked());
		}

		// The interact key with the shop modifier held opens the shop instead of starting the conversation.
		private static bool UsePrefix(InteractObjects __instance)
		{
			var me = Instance;
			if (me == null) return true;
			try
			{
				var talk = __instance.interactable as InteractableTalk;
				if (talk == null || me.VendorOf(talk) == null) return true;
				KeyboardShortcut k = me.shopKey.Value;
				if (k.Modifiers.Count() == 0 || !me.ModifiersHeld()) return true;
				if (Time.frameCount != me.openedFrame) me.TryOpen(talk);
				return false;
			}
			catch (Exception e) { Log.LogWarning("Shop key: " + e.Message); return true; }
		}

		private static MethodInfo hideName;

		// "Arvid Vinter (Talk)" gets the shop hint.
		private static void UseMessagePostfix(InteractableTalk __instance)
		{
			var me = Instance;
			if (me == null || __instance == null || __instance.talkDisabled) return;
			try
			{
				if (me.VendorOf(__instance) == null || UseText.instance == null) return;
				if (hideName == null) hideName = AccessTools.Method(typeof(InteractableTalk), "HideName");
				bool hidden = hideName != null && (bool)hideName.Invoke(__instance, null);
				UseText.instance.ChangeUseText(hidden ? "???" : __instance.title, __instance.useMessage + "   " + me.KeyLabel() + ": Shop");
			}
			catch (Exception) { }
		}

		private string KeyLabel()
		{
			KeyboardShortcut k = shopKey.Value;
			var parts = k.Modifiers.Select(m => m == KeyCode.LeftShift || m == KeyCode.RightShift ? "Shift" : m == KeyCode.LeftControl || m == KeyCode.RightControl ? "Ctrl" : m == KeyCode.LeftAlt || m == KeyCode.RightAlt ? "Alt" : m.ToString()).ToList();
			parts.Add(k.MainKey.ToString());
			return string.Join("+", parts.ToArray());
		}

		// ---- For other mods (Special Orders) ----

		/// <summary>The shop (the game's Trade) of the vendor with this name, built if needed and stocked for
		/// today; null if that person doesn't run a corner shop.</summary>
		public static Trade ShopFor(string npcName)
		{
			var me = Instance;
			if (me == null || string.IsNullOrEmpty(npcName) || !me.IsVendorName(npcName)) return null;
			try
			{
				Shop shop = me.GetShop(npcName.Trim());
				int day = TimeOfDayAzure.instance != null ? TimeOfDayAzure.instance.dayCounter : 0;
				if (shop.Shelf.stockedDay != day) { me.Restock(shop); shop.Shelf.stockedDay = day; }
				return shop.Till;
			}
			catch (Exception e) { Log.LogWarning("ShopFor " + npcName + ": " + e.Message); return null; }
		}

		/// <summary>Everything the corner shops stock (the config list), for ordering in bulk.</summary>
		public static List<Item> StockItems()
		{
			var me = Instance;
			var list = new List<Item>();
			if (me == null) return list;
			foreach (var kv in me.StockList()) if (!list.Contains(kv.Key)) list.Add(kv.Key);
			return list;
		}

		/// <summary>True for a till made by this mod.</summary>
		public static bool IsCornerShop(Trade t) { return t is ShopTrade; }

		// ---- Opening a shop ----

		private void TryOpen(InteractableTalk talk)
		{
			string name = VendorOf(talk);
			if (name == null) return;
			try
			{
				if (GameUIController.instance == null || GameUIController.instance.IsMenuVisible) return;
				if (Inventory.instance == null || Inventory.instance.storagePanelUI == null || TradePanel.instance == null) return;
				if (TradePanel.instance.activeTrade != null) return;
				openedFrame = Time.frameCount;
				Shop shop = GetShop(name);
				int day = TimeOfDayAzure.instance != null ? TimeOfDayAzure.instance.dayCounter : 0;
				if (shop.Shelf.stockedDay != day)
				{
					Restock(shop);
					shop.Shelf.stockedDay = day;
				}
				shop.Till.allowedCategories = buysFromPlayer.Value ? new string[0] : new[] { "_item_CornerShopsNothing" };
				shop.Shelf.BindPanel();
				shop.Till.StartTrade();
				Log.LogInfo("Opened " + name + "'s shop.");
			}
			catch (Exception e) { Log.LogError("Opening " + name + "'s shop: " + e); }
		}

		// Before the game saves or loads: every vendor's shop exists, so its stock is written and read.
		private static void BeforeSaveLoad()
		{
			var me = Instance;
			if (me == null) return;
			try
			{
				foreach (string n in me.VendorNames()) me.Build(n);
			}
			catch (Exception e) { Log.LogWarning("Shops before save/load: " + e.Message); }
		}

		private IEnumerable<string> VendorNames()
		{
			IsVendorName("x");   // refresh the parsed list
			return vendorNames.ToList();
		}

		private Shop GetShop(string name)
		{
			Shop shop = Build(name);
			// A new character, or a different save: its own shops start fresh.
			string me = CurrentCharacter();
			if (!string.IsNullOrEmpty(me) && shop.Shelf.ownerCharacter != me)
			{
				if (string.IsNullOrEmpty(shop.Shelf.ownerCharacter)) shop.Shelf.ownerCharacter = me;
				else shop.Shelf.Reset(me);
			}
			return shop;
		}

		private Shop Build(string name)
		{
			Shop shop;
			string key = vendorNames != null ? vendorNames.FirstOrDefault(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)) ?? name : name;
			if (shops.TryGetValue(key, out shop) && shop.Root != null) return shop;
			name = key;
			int slots = 64;
			try { if (ItemDatabase.instance != null) slots = Mathf.Clamp(StockList().Count + 24, 16, 88); } catch (Exception) { }
			var go = new GameObject("CornerShop_" + name.Replace(' ', '_'));
			go.SetActive(false);
			UnityEngine.Object.DontDestroyOnLoad(go);
			var shelf = go.AddComponent<ShopShelf>();
			string first = name.Split(' ')[0];
			shelf.Setup("CornerShops_" + name.Replace(' ', '_'), first + "'s Shop", slots);
			var till = go.AddComponent<ShopTrade>();
			till.Setup(shelf, first + "'s Shop", buyFactor.Value, sellFactor.Value, traderMoney.Value, traderMoney.Value);
			shelf.Till = till;
			SaveController.DontDestroyOnLoadObjects.Add(go);
			go.SetActive(true);
			shop = new Shop { Name = name, Root = go, Shelf = shelf, Till = till };
			shops[name] = shop;
			return shop;
		}

		private List<KeyValuePair<Item, int>> StockList()
		{
			var list = new List<KeyValuePair<Item, int>>();
			foreach (string raw in (stock.Value ?? "").Split(','))
			{
				string entry = raw.Trim();
				if (entry.Length == 0) continue;
				int amount = 1;
				int colon = entry.LastIndexOf(':');
				string title = entry;
				if (colon > 0)
				{
					int.TryParse(entry.Substring(colon + 1).Trim(), out amount);
					title = entry.Substring(0, colon).Trim();
				}
				Item item = FindItem(title);
				if (item == null) { WarnOnce("missing:" + title, "Corner Shops: no item called '" + title + "' (check the spelling in the config)."); continue; }
				list.Add(new KeyValuePair<Item, int>(item, Mathf.Max(1, amount)));
			}
			return list;
		}

		private static Item FindItem(string title)
		{
			if (ItemDatabase.instance == null) return null;
			List<Item> found = ItemDatabase.instance.FindByString(title);
			return found.FirstOrDefault(i => string.Equals(i.Title, title, StringComparison.OrdinalIgnoreCase));
		}

		// The day's stock: the shelf is cleared (what you sold them goes too, like the game's shops at restock)
		// and refilled from the config list; their cash is topped up for the day.
		private void Restock(Shop shop)
		{
			shop.Shelf.Clear();
			foreach (var kv in StockList())
			{
				try { shop.Shelf.AddItem(kv.Key, kv.Value, -1); }
				catch (Exception e) { WarnOnce("add:" + kv.Key.Title, "Corner Shops: couldn't stock " + kv.Key.Title + ": " + e.Message); }
			}
			shop.Till.MoneyRestock();
		}

		internal static string CurrentCharacter()
		{
			try
			{
				string n = Traverse.Create(typeof(SaveController)).Field("CharacterName").GetValue<string>();
				if (string.IsNullOrEmpty(n)) return null;
				n = n.Replace('\\', '/').TrimEnd('/');
				int slash = n.LastIndexOf('/');
				return slash >= 0 ? n.Substring(slash + 1) : n;
			}
			catch (Exception) { return null; }
		}

		private static readonly HashSet<string> warned = new HashSet<string>();
		private static void WarnOnce(string key, string msg) { if (warned.Add(key)) Log.LogWarning(msg); }
	}
}
