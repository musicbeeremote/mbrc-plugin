# Changelog

## [Unreleased]

### Changed
- API optimizations.

### Added
- Adds support for podcast retrieval to the protocol.

### Removed
- Drops protocol support for legacy search commands. (legacy clients that use the library search api won't work properly)
- Removes support for the ```nowplayinglistsearch``` api call.

## [1.1.0](https://github.com/musicbeeremote/plugin/compare/v1.0.0..v1.1.0) - 2017-01-24

### Changed
- Fixes an issue with the rating when using specific locales (like German)
- Fixes an issue with the favorite state not updating properly when changing tracks

### Added
- Adds a check to avoid a case where invalid characters in the tags would result in a sync failure.
- Adds a proper socket checker to update the status.
- Adds protocol support for switching audio outputs
- Adds protocol support for getting Radio Stations

## [1.0.0](https://github.com/musicbeeremote/plugin/compare/v1.0.0-beta.2..v1.0.0) - 2016-12-15

### Changed
- Makes the discovery listen to all available interfaces.
- Fixes an issue where the last.fm love status was the opposite of the expected.

### Added
- Adds new API for playlist retrieval.
- Adds new API for now playing that works with pagination.
- Adds debug checkbox on the Plugin settings options menu.
- Adds settings button to easily open the log.
- Adds new paginated API to enable library browsing.

### Removed

- Removed settings for now playing. The old call is now hard limited to 5000 (will be deprecated).

## [1.0.0-beta.2](https://github.com/musicbeeremote/plugin/compare/v1.0.0-beta.1..v1.0.0-beta.2) - 2016-11-28

### Changed 
-- Adds a couple of new APIs for the newest version of the application

## [1.0.0-beta.1](https://github.com/musicbeeremote/plugin/compare/v0.13.0..v1.0.0-beta.1) - 2016-09-15

### Changed
- Makes the discovery listen to all available interfaces.
- Fixes an issue where the last.fm love status was the opposite of the expected.

### Added
- Adds new API for playlist retrieval.
- Adds new API for now playing that works with pagination.
- Adds debug checkbox on the Plugin settings options menu.
- Adds settings button to easily open the log.
- Adds new paginated API to enable library browsing.

### Removed
- Removed settings for now playing. The old call is now hard limited to 5000 (will be deprecated).
   
## [0.13.0](https://github.com/musicbeeremote/plugin/compare/0.12.0..v0.13.0) - 2015-04-26

### Changed
- Fixes an issue that would cause the client to ignore data send by the plugin.

### Added
- Also adds a few new things on the protocol for version 2.1
 
## [0.12.0](https://github.com/musicbeeremote/plugin/compare/0.9.8..0.12.0) - 2015-04-06
 
### Changed
 - Adds autodj support to shuffle.

## [0.9.8](https://github.com/musicbeeremote/plugin/compare/0.9.7..0.9.8) - 2014-04-20
 
### Changed
 - Fixes an issue introduced in v0.9.7, making the queuing of tracks impossible.

## [0.9.7](https://github.com/musicbeeremote/plugin/compare/0.9.6..0.9.7) - 2014-04-08
 
### Changed
 - Fixes issue with queuing order of tracks,
 - Fixes the plugin part for the empty cover reset (also requires android application fix)
 
## [0.9.6](https://github.com/musicbeeremote/plugin/compare/0.9.5..0.9.6) - 2013-10-17
 
### Changed
 - Fixes a minor issue that happens on an empty list
 
## [0.9.5](https://github.com/musicbeeremote/plugin/compare/0.9.3..0.9.5) - 2013-10-02
 
## [0.9.3](https://github.com/musicbeeremote/plugin/compare/0.9.2..0.9.3) - 2013-08-11

## [0.9.2](https://github.com/musicbeeremote/plugin/compare/0.9.1..0.9.2) - 2013-08-01

## [0.9.1](https://github.com/musicbeeremote/plugin/compare/0.2.5..0.9.1) - 2013-07-31

## [0.2.5](https://github.com/musicbeeremote/plugin/compare/0.2.4..0.2.5) - 2013-06-17

## [0.2.4](https://github.com/musicbeeremote/plugin/releases/tag/0.2.4) - 2013-06-17



