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
		private static readonly FieldInfo startItemsField = AccessTools.Field(typeof(Storage), "startItems");
		private static readonly FieldInfo startDoneField = AccessTools.Field(typeof(Storage), "startDone");
		private static readonly FieldInfo slotPanelField = AccessTools.Field(typeof(Storage), "slotPanel");
		private static readonly FieldInfo dropValuesField = AccessTools.Field(typeof(Storage), "dropItemValues");

		public void Setup(string shelfTitle, int slots)
		{
			GUID = "";
			global = false;
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

		// Not saved: the shelf is refilled by the mod.
		public override bool OnSavingGame() { return true; }
		public override bool OnSavingGameSpecial() { return true; }
		public override bool OnLoadingGame() { return true; }
		public override bool OnLoadingGameSpecial() { return true; }
		public override bool OnLoadingGameDestructibleListCheck() { return true; }
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

		public override bool OnSavingGame() { return true; }
		public override bool OnSavingGameSpecial() { return true; }
		public override bool OnLoadingGame() { return true; }
		public override bool OnLoadingGameSpecial() { return true; }
		public override bool OnLoadingGameDestructibleListCheck() { return true; }
	}
}
