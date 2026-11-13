using System;
using System.Reflection;
using HarmonyLib;
using OS.Items;
using UnityEngine;

namespace CornerShopsMod
{
	/// <summary>
	/// The shop's shelf: a game <see cref="Storage"/> built at runtime, like Bank Inventory's bank. The game's own
	/// start-up and save hooks are skipped (the shelf is refilled by the mod and isn't saved), and every field the
	/// game reads without a null check gets a value.
	/// </summary>
	public class ShopShelf : Storage
	{
		// Saved with the shelf (public fields go into the save like the game's own storages).
		public int stockedDay = int.MinValue;   // in-game day the shelf was last restocked
		public float cash = -1f;                // the vendor's cash for buying from you
		public string ownerCharacter = "";      // the character whose game this stock belongs to
		[NonSerialized] public ShopTrade Till;

		private static readonly FieldInfo startItemsField = AccessTools.Field(typeof(Storage), "startItems");
		private static readonly FieldInfo startDoneField = AccessTools.Field(typeof(Storage), "startDone");
		private static readonly FieldInfo slotPanelField = AccessTools.Field(typeof(Storage), "slotPanel");
		private static readonly FieldInfo dropValuesField = AccessTools.Field(typeof(Storage), "dropItemValues");

		public void Setup(string guid, string shelfTitle, int slots)
		{
			GUID = guid;
			global = true;
			title = shelfTitle;
			useMessage = "Shop";
			interactDisabled = true;
			disableSpoil = true;
			spawnSettings = new SpawnSettings();
			_Lock = new Lock();
			if (openSound == null) openSound = new SoundInfo();
			if (closeSound == null) closeSound = new SoundInfo();
			if (lockedSound == null) lockedSound = new SoundInfo();
			if (startItemsField != null) startItemsField.SetValue(this, new ItemStack[0]);
			if (dropValuesField != null && dropValuesField.GetValue(this) == null)
			{
				try { dropValuesField.SetValue(this, Activator.CreateInstance(dropValuesField.FieldType)); } catch (Exception) { }
			}
			storageSlots = new StorageSlot[slots];
			for (int i = 0; i < slots; i++) storageSlots[i] = new StorageSlot();
			Slots = new SlotController[slots];
			for (int i = 0; i < slots; i++)
			{
				Slots[i] = new SlotController();
				Slots[i].itemStack = storageSlots[i];
			}
			for (int i = 0; i < slots; i++) Slots[i].Siblings = Slots;
			if (startDoneField != null) startDoneField.SetValue(this, true);
			initialized = true;
		}

		/// <summary>The panel the shelf's slots are drawn in (the game's storage panel).</summary>
		public void BindPanel()
		{
			if (slotPanelField != null && Inventory.instance != null && Inventory.instance.storagePanelUI != null)
				slotPanelField.SetValue(this, Inventory.instance.storagePanelUI.storageSlotPanel.transform);
		}

		public void Clear()
		{
			foreach (StorageSlot s in storageSlots)
			{
				s.itemId = -1;
				s.itemAmount = 0;
				s.ownerId = -1;
				s.Meta = null;
				s.itemReference = null;
			}
		}

		public void RebuildSlots()
		{
			if (storageSlots == null) storageSlots = new StorageSlot[0];
			Slots = new SlotController[storageSlots.Length];
			for (int i = 0; i < Slots.Length; i++)
			{
				if (storageSlots[i] == null) storageSlots[i] = new StorageSlot();
				var sc = new SlotController { itemStack = storageSlots[i] };
				try { sc.itemStack.SetItemReference(); } catch (Exception) { }
				Slots[i] = sc;
			}
			for (int i = 0; i < Slots.Length; i++) Slots[i].Siblings = Slots;
		}

		/// <summary>Forget the stock (a new game, or a save from before the stock was saved): the next
		/// visit restocks.</summary>
		public void Reset(string character)
		{
			Clear();
			stockedDay = int.MinValue;
			cash = -1f;
			ownerCharacter = character ?? "";
		}

		// Saved in the save's global data through the hooks for objects that live across scenes, like the
		// Bank Inventory bank: what's on the shelf (including what you sold them), the restock day and their cash.
		public override bool OnSavingGame() { return true; }
		public override bool OnLoadingGame() { return true; }
		public override bool OnLoadingGameDestructibleListCheck() { return true; }

		public override bool OnSavingGameSpecial()
		{
			if (Till != null) cash = Till.Cash;
			if (string.IsNullOrEmpty(ownerCharacter)) ownerCharacter = CornerShopsPlugin.CurrentCharacter() ?? "";
			return SaveController.SerializeData(this, global);
		}

		public override bool OnLoadingGameSpecial()
		{
			bool ok = false;
			try { ok = SaveController.DeSerializeData(this, global); } catch (Exception) { ok = false; }
			if (ok)
			{
				RebuildSlots();
				if (Till != null && cash >= 0f) Till.Cash = cash;
				ownerCharacter = CornerShopsPlugin.CurrentCharacter() ?? ownerCharacter;
			}
			else
			{
				RebuildSlots();
				Reset(CornerShopsPlugin.CurrentCharacter());
			}
			return true;
		}
	}

	/// <summary>The shop's till: the game's own <see cref="Trade"/>, so buying, selling, prices, the trade
	/// screen and its wealth line all work as with the game's traders.</summary>
	public class ShopTrade : Trade
	{
		private static readonly FieldInfo storageField = AccessTools.Field(typeof(Trade), "storage");
		private static readonly FieldInfo startDoneField = AccessTools.Field(typeof(Trade), "startDone");

		public void Setup(ShopShelf shelf, string shopTitle, float buyFactor, float sellFactor, int moneyMin, int moneyMax)
		{
			GUID = "";
			title = shopTitle;
			useMessage = "Shop";
			isInteractable = true;
			open = true;
			autoOpenClose = false;
			owner = new NPC.NPCReference(-1);
			basePurchasePrice = buyFactor;
			baseSellPrice = sellFactor;
			sellPriceMode = PriceMode.OnlyOC;
			purchasePriceMode = PriceMode.OnlyOC;
			buysStolen = false;
			allowedCategories = new string[0];
			forbiddenCategories = new string[0];
			traderMoneyAmountMin = moneyMin;
			traderMoneyAmountMax = Mathf.Max(moneyMin, moneyMax);
			traderMoneySpawnSettings = new SpawnSettings();
			if (storageField != null) storageField.SetValue(this, shelf);
			if (startDoneField != null) startDoneField.SetValue(this, true);   // skip Trade.Start (opening hours, timers)
		}

		/// <summary>The vendor's cash for buying from you (the game's own trader money).</summary>
		public float Cash { get { return currentMoney; } set { currentMoney = value; } }

		public override bool OnSavingGame() { return true; }
		public override bool OnSavingGameSpecial() { return true; }
		public override bool OnLoadingGame() { return true; }
		public override bool OnLoadingGameSpecial() { return true; }
		public override bool OnLoadingGameDestructibleListCheck() { return true; }
	}
}
