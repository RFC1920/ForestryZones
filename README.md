# ForestryZones
Places forest-friendly zones around building privilege

Requires: ZoneManager

Uses: Friends, Clans, and Rust teams

The area of protection is based on a ZoneManager zone surrounding the TC, not on building privilege, so ZoneManager is required.

## Permissions
One optional permission exists, forestryzones.use.  If the configuration setting "Require permission to prevent harvesting of trees" is set to true, only players with this permission will have trees surrounding their tool cupboards protected from harvesting by strangers.

## Configuration
```js
{
  "Require permission to prevent harvesting of trees": false,
  "Allow building owner to harvest trees": false,
  "Use Friends plugin to allow harvesting by friends": false,
  "Use Clans plugin to allow harvesting by clan members": false,
  "Use Rust Teams to allow harvesting by team members": false,
  "Message to send to offending player": "This area is protected by the local Forestry Service.",
  "Radius of zone around building": 120.0,
  "debug": false,
  "Version": {
    "Major": 1,
    "Minor": 0,
    "Patch": 2
  }
}
```

The default configuration does not require that permissions be set.  All placed tool cupboards will have a zone with a 120m radius surrounding them inside which no one can harvest trees.

To allow only the owner to harvest trees in this range, set "Allow building owner to harvest trees" to true.

To allow their friends, clan or team members to harvest, set one or more of the other associated configs to true.

Adjust the radius as desired to fit your play style and map.

Anyone hitting a tree in a protected zone will get the "Message to send to offending player" message once per zone per plugin load.

## Inspiration and notes
The idea is to keep a few trees around and maintain the beauty of Rust for PVE and casual players.

Of course, some players can and will likely abuse this concept and may place TCs all over the map prevent anyone from getting any wood at all.  We may implement a feature to help control this, but there are other plugins out there perhaps better suited to this kind of management.

It may be best to require the permission.  Create a new group to manage that permission and add your new users to it by default if you like.  In this way you can at least remove any offenders as needed.

If this does get to be a problem, I can try to work on additional safeguards to help.

