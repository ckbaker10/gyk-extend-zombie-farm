# gyk-extend-zombie-farm

BepInEx-5-Plugin für Graveyard Keeper (Version 1): erlaubt auf der Zombie-Farm
alle Samen, die auch im normalen Beet wachsen (z. B. Hanf). Spalier-Pflanzen
(Wein, Hopfen) bleiben ausgenommen.

## Stand

- 0.1.0: Analysephase. Das Plugin schreibt beim Laden der Spielbalance
  `balance-dump.txt` neben die DLL (Crafts `garden_*` und Zombie-Beet-Objekte).
- Verifiziert (2026-11-22, `resources.assets`): Zombie-Beet hat Crafts für
  Weizen, Kohl, Karotte, Rübe, Linsen, Kürbis, Zwiebel. Im Spielerbeet gibt es
  zusätzlich Hanf (`hamp_seed`, `garden_cannabis_*`); Wein/Hopfen brauchen Spalier.
- Hypothese: Zombie-Crafts wechseln per `change_wgo` in ein pflanzenspezifisches
  Objekt (`zombie_garden_desk_<crop>`); für Hanf existiert keines. Wird mit dem Dump geprüft.

## Bauen / Installieren

```sh
dotnet build src -c Release -p:GameDir="<Spielordner>"
cp src/bin/Release/net472/ExtendZombieFarm.dll "<Spielordner>/BepInEx/plugins/ExtendZombieFarm/"
```
