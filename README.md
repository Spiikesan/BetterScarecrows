## Features

Repair broken scarecrows by :
* Allowing them to attack
* Allowing them to forget you if you are far enough
* Enable grenade throwing (via the original ConVar halloween.scarecrows_throw_beancans, defaults to true)
* Set the delay between grenade throwing (via the original ConVar halloween.scarecrow_throw_beancan_global_delay, defaults to 8). There is also a 10% chance to throw.
* Repair their life. Note that multipliers doesn't work anymore so they have a little bit more life by default.
* Repair they reach distance.
* Repair their sounds (note: The chainsaw is still silent)

Improve scarecrows by :
* Allowing them to roam like animals. No more dormant zombies !
* Attacking when they are attacked. They respond to try to stay alive, and are not "passive" anymore if hit when they didn't see you.
* Flee from non human threat that are attacking them (like cactus). They will not have time to escape turrets, tho.

## Configuration

All the configuration is done through server config variables.

Set the amount of zombies per square kilometer with the **halloween.scarecrowpopulation** to allow them spawn all around the map (near structures)

**halloween.scarecrows_throw_beancans** (true by default) allow them to throw their grenades and **halloween.scarecrow_throw_beancan_global_delay** to set the minimum delay between throws (default 8).

**aimanager.ai_dormant** (default true) enable the capacity of scarecrows to "sleep" if there is no player in the **aimanager.ai_to_player_distance_wakeup_range** range (default to 160). It is greatly recommended to save server resources.
You can also set the aimanager.ai_to_player_distance_wakeup_range to a lower value to save a little bit more resources (80 seems to be sufficient). Also, please note that the distance doesn't use the height axis. (it's only x and z).

## Credits

- **Spiikesan**, the author of the plugin