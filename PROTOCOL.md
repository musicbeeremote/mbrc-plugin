MusicBee Remote Protocol for v1.0.0
______

This is the JSON based protocol (over TCP socket) of MusicBee Remote v1.x 

**Revision**: 5

> WIP documenting the latest version of mbrc-protocol

## Basic Information about the protocol

All the messages send over the protocol should be new line terminated ```\r\n```.

## Handshake

In order for a client to start getting data over the TCP socket the client 
  has to complete the handshake process first.

 After connecting to the plugin the client has to inform the plugin about the client.
 For example the android client will send the following. *The data part in this message is 
  optional and it can be safely skipped. This might change in the future.*
  
  ### Player
  ####Request
```json
{"context":"player","data":"Android"}
```

#### Response
After sending the ```player``` message to the plugin the plugin with send back a player response.
```json
{"context":"player","data":"MusicBee"}
```
The data field is currently unused, however if by any change this plugin is used in the future for any other player.
This will be probably used to hide player specific features on the client.

### Protocol
After getting the ```player``` response from the server the next message must be a protocol message.
If the next message is anything else, the plugin will terminate the connection of the current socket.
#### Request
The default socket connection should have the ```no_broadcast``` field set to ```false``` in order to 
receive status update notification from the plugin and inform the client about changes that are happening on **MusicBee**.

The ```protocol_version``` affects the responses of the plugin. If you have an earlier version the plugin might work differently
for compatibility reasons.

The ```client_id``` should be a ```UUID``` or some kind of unique identifier that the client will send to indentify itself on the plugin.
This will be used in the party mode, when it wil be ready in order to identify the clients and permit the various actions.
```json
{"context":"protocol","data":{"no_broadcast":true,"protocol_version":5,"client_id":"test"}}
```
#### Response
The plugin will send a protocol response that contains the latest protocol revision supported by the plugin.
```json
{"context":"protocol","data":5}
```
###

## No broadcast requests
If it is not specified explicitly the client socket will receive broadcasts
whenever there is some chang on the plugin side. 

Since Protocol Revision **3** the protocol adds support for ```no_broadcast``` socket connections.
The ```no_broadcast``` socket connection is specified when the client send the protocol information during the handshake.

A ```no_broadcast``` connection with the plugin will only answer when a request is send. No status updates will be posted.

## Podcasts

Since the r5 the mbrc protocol supports the retrieval of podcasts from MusicBee. This requires at least ```v3.1.6322``` in order to work.

### Subscriptions

### Episodes

### Artwork
The protocol has support for retrieving the artwork (cover) of a podcasts subscription.

#### Request
In order to retrieve a podcast subscriptions artwork you have to send the following request through the socket.
The id is retrieved from the ```id``` field of the **Subscription**
```json
{"context":"podcastartwork","data":{"id": "https://rss.simplecast.com/podcasts/1684/rss"}}
```
#### Response
The response of this request will contain the artwork in the ```data``` field as a ```base64``` encoded ```jpeg``` image
````json
{"context":"podcastartwork","data":{"artwork":"base64encoded"}}
````