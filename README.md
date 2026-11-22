# gyk-extend-zombie-farm

BepInEx-5-Plugin für Graveyard Keeper (Version 1): erlaubt auf der Zombie-Farm
alle Samen, die auch im normalen Beet wachsen (z. B. Hanf). Spalier-Pflanzen
(Wein, Hopfen) bleiben ausgenommen.

## Funktion (1.0.0)

Beim Laden der Spielbalance werden für jede Pflanze des normalen Beets ohne
Spalier, die dem Zombie-Beet fehlt (derzeit Hanf), der einstufige Karotten-Craft
`garden_carrot_grow_desk_planting` geklont (wie Karotte/Weizen/Kohl/Rübe nur
eine Qualitätsstufe, Items ohne `:n`): gleiche Beetstufen, Kosten (24 Samen),
Ernte, Samen und Stroh, nur mit Hanf-Items.

Verifiziert per Balance-Dump (2026-11-22): Zombie-Crafts sind eigenständig
(`craft_in` = `zombie_garden_desk_0/1/2`, kein `change_wgo`). Ungetestet im
Spiel: Anzeige des Beets. Config `Visual Crop` (leer) bestimmt das
Sprite, da es für Hanf keines gibt.

## Bauen / Installieren

```sh
dotnet build src -c Release -p:GameDir="<Spielordner>"
cp src/bin/Release/net472/ExtendZombieFarm.dll "<Spielordner>/BepInEx/plugins/ExtendZombieFarm/"
```
