using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CornerShopsMod
{
	/// <summary>
	/// A "Sort" button on a corner shop's trade screen (the game hides its own Sort button while trading).
	/// It sorts the shop's shelf with the game's sort, so Sort By's chosen order applies when that mod is installed.
	/// </summary>
	internal class ShopSortButton : MonoBehaviour
	{
		private GUIStyle style;
		private static MethodInfo sortByLabel;
		private static FieldInfo sortByMode;
		private static bool looked;

		private static ShopTrade OpenShop()
		{
			try
			{
				if (TradePanel.instance == null || !Cursor.visible) return null;
				return TradePanel.instance.activeTrade as ShopTrade;
			}
			catch (Exception) { return null; }
		}

		private static Texture2D Tex(Color fill, Color edge)
		{
			var t = new Texture2D(8, 8) { hideFlags = HideFlags.HideAndDontSave };
			for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) t.SetPixel(x, y, x == 0 || y == 0 || x == 7 || y == 7 ? edge : fill);
			t.Apply();
			return t;
		}

		// "Sort (Name)" when Sort By is installed, so you can see which order it uses.
		private static string Caption()
		{
			if (!looked)
			{
				looked = true;
				Type p = AccessTools.TypeByName("SortByMod.SortByPlugin");
				if (p != null) { sortByMode = AccessTools.Field(p, "Mode"); sortByLabel = AccessTools.Method(p, "Label"); }
			}
			try
			{
				if (sortByMode != null && sortByLabel != null)
				{
					object entry = sortByMode.GetValue(null);
					object mode = entry != null ? entry.GetType().GetProperty("Value").GetValue(entry, null) : null;
					if (mode != null) return "Sort: " + sortByLabel.Invoke(null, new[] { mode });
				}
			}
			catch (Exception) { }
			return "Sort";
		}

		private void OnGUI()
		{
			ShopTrade shop = OpenShop();
			if (shop == null || Inventory.instance == null || Inventory.instance.storagePanelUI == null) return;
			Rect panel;
			try { panel = ScreenRect(Inventory.instance.storagePanelUI.transform as RectTransform); } catch (Exception) { return; }
			if (panel.width < 50f) return;
			if (style == null || style.normal.background == null)
			{
				style = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, padding = new RectOffset(12, 12, 6, 6), border = new RectOffset(2, 2, 2, 2) };
				style.normal.background = Tex(new Color32(92, 70, 44, 245), new Color32(66, 50, 32, 255));
				style.hover.background = Tex(new Color32(120, 92, 58, 245), new Color32(201, 160, 72, 255));
				style.active.background = Tex(new Color32(201, 160, 72, 245), new Color32(122, 92, 40, 255));
				style.normal.textColor = style.hover.textColor = new Color32(240, 228, 205, 255);
				style.active.textColor = new Color32(43, 36, 24, 255);
			}
			string text = Caption();
			Vector2 size = style.CalcSize(new GUIContent(text));
			// In the shop's title strip, between the name and the wealth line.
			var r = new Rect(panel.x + panel.width * 0.56f - size.x / 2f, panel.y + 26f, size.x, size.y);
			if (GUI.Button(r, new GUIContent(text, "Sort the shop's shelf"), style)) Sort(shop);
		}

		private static void Sort(ShopTrade shop)
		{
			try
			{
				ShopShelf shelf = shop.GetComponent<ShopShelf>();
				if (shelf == null || shelf.Slots == null || Inventory.instance == null) return;
				Inventory.instance.SortSlots(shelf.Slots);
				foreach (SlotController sc in shelf.Slots) if (sc != null && sc.slot != null) sc.slot.UpdateSlot();
			}
			catch (Exception e) { CornerShopsPlugin.Log.LogWarning("Sorting the shop: " + e.Message); }
		}

		private static Rect ScreenRect(RectTransform rt)
		{
			var corners = new Vector3[4];
			rt.GetWorldCorners(corners);
			Canvas canvas = rt.GetComponentInParent<Canvas>();
			Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
			Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
			Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
			return new Rect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y), Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
		}
	}
}
