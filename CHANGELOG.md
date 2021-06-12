Change Log
---------
# Versions

## 1.4.1 - 2021/06/12
### Changed
- Introduces state persistence for the cover caching mechanism to improve performance.

### Added
- Adds a button in the control panel to allow for easy cache invalidation.

## 1.4.0
### Changed
- Fixes status displaying as stopped when range filtering is active.
- Adds pagination to the radio station api
- Adds support for different behavior on different client platforms (Android/iOS)
- Fixes repeat one functionality.
- Fixes issue with lyrics initialization on direct request.
- Fixes off by one now playing play on Android clients
- Adds Album Artist info to `nowplayinglist` and `libraryalbumtracks` commands.

### Added
- Adds support for requesting list of Album Artists instead of Artists.
- Adds support for shuffle/non-shuffle play all command. 
- Adds support for Album covers.

## 1.3.0-ios
### Changed
- Adds disk number to `libraryalbumtracks`.

### Added
- Introduces tag manipulation command.

## 1.2.1-ios
### Changed
- Introduces ordering into the now playing list and a limit of 100 entries.

## 1.2.0-ios
### Changed
- Allows the reset of a track's rating by sending an empty string.

### Added
- Introduces support for playing track details.

## 1.1.0
- Adds a check to avoid a case where invalid characters in the tags would result in a sync failure.
- Adds a proper socket checker to update the status.
- Fixes an issue with the rating when using specific locales (like German)
- Fixes an issue with the favorite state not updating properly when changing tracks
- Adds protocol support for switching audio outputs
- Adds protocol support for getting Radio Stations

## 1.0.0
- Adds new API for playlist retrieval.
- Adds new API for now playing that works with pagination.
- Removed settings for now playing. The old call is now hard limited to 5000 (will be deprecated).
- Adds debug checkbox on the Plugin settings options menu.
- Adds settings button to easily open the log.
- Makes the discovery listen to all available interfaces.
- Adds new paginated API to enable library browsing.
- Fixes an issue where the last.fm love status was the opposite of the expected.
