Change Log
---------
# Versions

## 1.1.0
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
