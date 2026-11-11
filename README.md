# Obenseuer Corner Shops

A BepInEx 6 mod for Obenseuer: the hotel and motel keepers sell everyday supplies.

- **Who:** Arvid Vinter, Carl Hagen (Hotel Korso, Bazaar) and Joran Kallio (The Pickaxe Motel, mines).
- **How:** look at one of them and press **Shift+E**. The game's own trade screen opens with their stock. Plain **E** still talks to them.
- **Stock:** food (breads, canned food, sausages, chips, chocolate), drinks (sodas, ice tea, milk), utility items (can opener, matchbox, lighter, candles, batteries, toilet paper, soap, bandages, painkillers, duct tape, glue) and paperwork (paper, notebook, envelopes, stamps, ink, fancy paper). Refilled every in-game day.
- **Selling:** they also buy from you, with a daily cash amount, like the game's traders.
- Buying, prices, stolen-item rules and the trade screen are the game's own (`Trade`), so it behaves like any other trader.

## Config (`BepInEx/config/com.juanberta.obenseuer.cornershops.cfg`)

| Section | Key | Default | |
|---|---|---|---|
| General | Vendors | Arvid Vinter, Carl Hagen, Joran Kallio | NPC names that run a shop |
| General | ShopKey | LeftShift + E | opens the shop |
| Stock | Items | (36 items) | `Item name:amount, ...`, names as the game shows them |
| Prices | BuyPriceFactor | 1.25 | what you pay, times the item's value |
| Prices | SellPriceFactor | 0.6 | what they pay you |
| Prices | BuysFromPlayer | true | they buy from you |
| Prices | VendorCash | 2000 | OC they have each day to buy from you |

## Notes

- Stock isn't saved: it refills every new in-game day and after restarting the game (things you sold them are gone then, as with the game's shops at restock).
- Works with Bank Inventory (the storage search box works in the shop) and Diegetic UI.

## Build

`dotnet build -c Release` (set `ObenseuerDir` if the game isn't in the default Steam folder).
