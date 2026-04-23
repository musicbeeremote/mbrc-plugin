using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicBeeRemote.ApiDebugger.Constants;
using MusicBeeRemote.ApiDebugger.Models;
using MusicBeeRemote.ApiDebugger.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MusicBeeRemote.ApiDebugger.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly string[] ClientTypeItems = ["Android", "iOS", "Windows", "Generic"];
    private static readonly string[] ProtocolVersionItems = ["2", "2.1", "3", "4"];
    private static readonly string[] DataTypeItems = ["Empty", "String", "Boolean", "Number", "JSON"];

    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private readonly DiscoveryService _discoveryService;

    // Connection State
    private bool _isConnected;
    private bool _handshakeCompleted;
    private bool _playerAcknowledged;
    private string? _negotiatedProtocol;
    private string? _pendingProtocolVersion;
    private ClientType _pendingClientType;

    public MainWindowViewModel()
    {
        _discoveryService = new DiscoveryService();
        ProtocolCommands = new ObservableCollection<ProtocolCommand>(LoadProtocolCommands());

        // Set defaults
        SelectedClientTypeIndex = 0;
        SelectedProtocolVersionIndex = 2; // Default to protocol 4
        SelectedDataTypeIndex = 0;
        IpAddress = "127.0.0.1";
        Port = 3000;

        // Select first command (init) by default
        SelectedCommand = ProtocolCommands.FirstOrDefault();

        AddLogMessage("API Debugger started", LogMessageType.Info);
    }

    #region Observable Properties

    [ObservableProperty]
    private string _ipAddress = "127.0.0.1";

    [ObservableProperty]
    private int _port = 3000;

    [ObservableProperty]
    private int _selectedClientTypeIndex;

    [ObservableProperty]
    private int _selectedProtocolVersionIndex;

    [ObservableProperty]
    private bool _noBroadcast;

    [ObservableProperty]
    private string _statusText = "Disconnected";

    [ObservableProperty]
    private string _statusColor = "Red";

    [ObservableProperty]
    private bool _canConnect = true;

    [ObservableProperty]
    private bool _canDisconnect;

    [ObservableProperty]
    private bool _canSendCommands;

    [ObservableProperty]
    private ObservableCollection<LogMessage> _logMessages = new();

    [ObservableProperty]
    private ObservableCollection<LogMessage> _receivedOnlyMessages = new();

    [ObservableProperty]
    private bool _filterReceivedOnly;

    [ObservableProperty]
    private int _selectedLogIndex = -1;

    [ObservableProperty]
    private ObservableCollection<ProtocolCommand> _protocolCommands;

    [ObservableProperty]
    private ProtocolCommand? _selectedCommand;

    [ObservableProperty]
    private int _selectedDataTypeIndex;

    [ObservableProperty]
    private string _commandData = "";

    [ObservableProperty]
    private bool _isCommandDataEnabled;

    [ObservableProperty]
    private string _commandDataPlaceholder = "No data will be sent";

    [ObservableProperty]
    private string _jsonPayload = "";

    [ObservableProperty]
    private Bitmap? _coverImage;

    [ObservableProperty]
    private string _coverStatus = "No cover received";

    [ObservableProperty]
    private string _coverStatusColor = "Gray";

    [ObservableProperty]
    private string _lyricsText = "No lyrics received";

    [ObservableProperty]
    private string _lyricsStatus = "No lyrics loaded";

    [ObservableProperty]
    private string _lyricsStatusColor = "Gray";

    #endregion

    #region Static Collections

    public static IReadOnlyList<string> ClientTypes => ClientTypeItems;
    public static IReadOnlyList<string> ProtocolVersions => ProtocolVersionItems;
    public static IReadOnlyList<string> DataTypes => DataTypeItems;

    public ObservableCollection<LogMessage> CurrentMessages => FilterReceivedOnly ? ReceivedOnlyMessages : LogMessages;

    #endregion

    #region Commands

    [RelayCommand]
    private async Task DiscoverAsync()
    {
        CanConnect = false;
        AddLogMessage("Starting discovery...", LogMessageType.Info);
        try
        {
            var discovered = await _discoveryService.DiscoverAsync();
            if (discovered.Count > 0)
            {
                var first = discovered.First();
                IpAddress = first.IpAddress;
                Port = first.Port;
                AddLogMessage($"Found {discovered.Count} plugin(s). Using first: {first.DisplayName}", LogMessageType.Success);
            }
            else
            {
                AddLogMessage("No plugins discovered.", LogMessageType.Warning);
            }
        }
        catch (Exception ex)
        {
            AddLogMessage($"Discovery failed: {ex.Message}", LogMessageType.Error);
        }
        finally
        {
            CanConnect = true;
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        CanConnect = false;
        StatusText = "Connecting...";
        StatusColor = "Orange";

        try
        {
            var clientType = Enum.Parse<ClientType>(ClientTypeItems[SelectedClientTypeIndex]);
            var protocolVersion = ProtocolVersionItems[SelectedProtocolVersionIndex];

            AddLogMessage($"Starting connection to {IpAddress}:{Port}", LogMessageType.Info);
            AddLogMessage($"Client Type: {clientType}, Protocol: {protocolVersion}, No Broadcast: {NoBroadcast}", LogMessageType.Info);

            // Reset handshake state
            _handshakeCompleted = false;
            _playerAcknowledged = false;
            _negotiatedProtocol = null;
            _pendingProtocolVersion = protocolVersion;
            _pendingClientType = clientType;

            // Connect to TCP socket
            _tcpClient = new TcpClient();
            AddLogMessage("Connecting to TCP socket...", LogMessageType.Info);

            await _tcpClient.ConnectAsync(IpAddress, Port);
            _networkStream = _tcpClient.GetStream();

            _isConnected = true;
            AddLogMessage("TCP connection established", LogMessageType.Success);
            StatusText = "Connected, performing handshake...";

            // Start listening for messages first
            AddLogMessage("Starting message listener...", LogMessageType.Info);
            _ = Task.Run(ListenForMessages);

            await Task.Delay(100);

            // Send initial player message only
            AddLogMessage("Sending player message...", LogMessageType.Info);
            await SendPlayerMessage();

            AddLogMessage("Waiting for handshake...", LogMessageType.Info);

            UpdateConnectionState(true);
            StatusText = "Handshake in progress...";
        }
        catch (Exception ex)
        {
            AddLogMessage($"Connection failed: {ex.Message}", LogMessageType.Error);
            Disconnect();
        }
        finally
        {
            if (!_isConnected)
                CanConnect = true;
        }
    }

    [RelayCommand]
    private void ExecuteDisconnect()
    {
        Disconnect();
    }

    [RelayCommand]
    private async Task SendCommandAsync()
    {
        if (!_handshakeCompleted || SelectedCommand == null)
        {
            AddLogMessage("Please wait for handshake to complete", LogMessageType.Warning);
            return;
        }

        try
        {
            var dataType = DataTypeItems[SelectedDataTypeIndex];
            var data = ParseDataValue(dataType, CommandData);

            var message = new
            {
                context = SelectedCommand.Command,
                data
            };

            await SendMessage(JsonConvert.SerializeObject(message));
            AddLogMessage($"{SelectedCommand.Command}", LogMessageType.Sent);
        }
        catch (Exception ex)
        {
            AddLogMessage($"Error: {ex.Message}", LogMessageType.Error);
        }
    }

    [RelayCommand]
    private async Task SendJsonAsync()
    {
        if (!_handshakeCompleted)
        {
            AddLogMessage("Please wait for handshake to complete", LogMessageType.Warning);
            return;
        }

        try
        {
            // Parse and re-serialize to compact JSON (no newlines)
            var parsed = JToken.Parse(JsonPayload);
            var compactJson = parsed.ToString(Formatting.None);
            var context = parsed["context"]?.ToString() ?? "custom";
            await SendMessage(compactJson);
            AddLogMessage(context, LogMessageType.Sent);
        }
        catch (JsonException ex)
        {
            AddLogMessage($"Invalid JSON: {ex.Message}", LogMessageType.Error);
        }
    }

    [RelayCommand]
    private void PrettifyJson()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(JsonPayload))
            {
                AddLogMessage("Please enter some JSON to prettify", LogMessageType.Warning);
                return;
            }

            var parsed = JToken.Parse(JsonPayload);
            JsonPayload = parsed.ToString(Formatting.Indented);
            AddLogMessage("JSON prettified", LogMessageType.Success);
        }
        catch (JsonException ex)
        {
            AddLogMessage($"JSON prettify failed: {ex.Message}", LogMessageType.Error);
        }
    }

    [RelayCommand]
    private void ClearMessages()
    {
        LogMessages.Clear();
        ReceivedOnlyMessages.Clear();
    }

    [RelayCommand]
    private void TestCover()
    {
        try
        {
            // Create a simple test image using SkiaSharp (Avalonia's rendering backend)
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(100, 100));
            var canvas = surface.Canvas;

            // Blue background
            canvas.Clear(new SkiaSharp.SKColor(0, 0, 255));

            // Yellow circle
            using var paint = new SkiaSharp.SKPaint
            {
                Color = new SkiaSharp.SKColor(255, 255, 0),
                IsAntialias = true
            };
            canvas.DrawCircle(50, 50, 25, paint);

            // Encode to PNG
            using var image = surface.Snapshot();
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream();
            data.SaveTo(ms);
            ms.Position = 0;

            CoverImage = new Bitmap(ms);
            CoverStatus = "Test image loaded\nSize: 100x100";
            CoverStatusColor = "Green";
            AddLogMessage("Test image set successfully", LogMessageType.Success);
        }
        catch (Exception ex)
        {
            AddLogMessage($"Error setting test image: {ex.Message}", LogMessageType.Error);
        }
    }

    [RelayCommand]
    private void TestLyrics()
    {
        var testLyrics = """
            Test Lyrics

            [Verse 1]
            This is a test lyric
            To verify the display
            Works correctly now
            In every way

            [Chorus]
            Testing, testing, one two three
            Can you see the lyrics clearly?
            Multiple lines should display
            In this readonly text array

            [Verse 2]
            The text should scroll
            When content is long
            And this test proves
            Everything works strong

            [End]
            """;

        LyricsText = testLyrics;
        LyricsStatus = $"Test lyrics loaded - {testLyrics.Length} characters";
        LyricsStatusColor = "Green";
        AddLogMessage("Test lyrics set successfully", LogMessageType.Success);
    }

    #endregion

    #region Data Type Handling

    partial void OnSelectedCommandChanged(ProtocolCommand? value)
    {
        UpdateJsonPayload();
    }

    partial void OnFilterReceivedOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentMessages));
    }

    private void UpdateJsonPayload()
    {
        if (SelectedCommand == null)
            return;

        try
        {
            var data = GetCommandTemplate(SelectedCommand.Command);
            var message = new
            {
                context = SelectedCommand.Command,
                data
            };

            JsonPayload = JToken.FromObject(message).ToString(Formatting.Indented);
        }
        catch
        {
            var message = new
            {
                context = SelectedCommand.Command,
                data = new { }
            };
            JsonPayload = JToken.FromObject(message).ToString(Formatting.Indented);
        }
    }

    private static object GetCommandTemplate(string command)
    {
        return command switch
        {
            // Paginated list commands
            "nowplayinglist" or "browsegenres" or "browsealbums" or "browsetracks" or
            "playlistlist" or "radiostations" => new { offset = 0, limit = 50 },

            // Browse artists (special case with album_artists option)
            "browseartists" => new { offset = 0, limit = 50, album_artists = false },

            // Now playing queue
            "nowplayingqueue" => new { queue = "next", play = "false", data = new[] { "file_url" } },

            // Move track in now playing
            "nowplayinglistmove" => new { from = 0, to = 1 },

            // Tag change
            "nowplayingtagchange" => new { tag = "artist", value = "new value" },

            // Library queue commands
            "libraryqueuegenre" or "libraryqueueartist" or "libraryqueuealbum" or "libraryqueuetrack"
                => new { type = "next", query = "search term" },

            // Album cover request
            "libraryalbumcover" => new { album = "Album Name", artist = "Artist Name" },

            // Position (in milliseconds)
            "nowplayingposition" => 0,

            // Volume (0-100)
            "playervolume" => 50,

            // Play at index
            "nowplayinglistplay" or "nowplayinglistremove" => 0,

            // Boolean toggle commands
            "playermute" or "playershuffle" or "playerautodj" or "scrobbler" => "toggle",

            // Repeat mode
            "playerrepeat" => "None", // None, All, One

            // Rating
            "nowplayingrating" => "0", // 0-5

            // Last.fm rating
            "nowplayinglfmrating" => "love", // love, ban, toggle

            // Output switch
            "playeroutputswitch" => "Device Name",

            // Search commands (string query)
            "librarysearchartist" or "librarysearchalbum" or "librarysearchgenre" or
            "librarysearchtitle" or "libraryartistalbums" or "librarygenreartists" or
            "libraryalbumtracks" or "nowplayinglistsearch" or "playlistplay" => "search term",

            // Library play all
            "libraryplayall" => false,

            // Protocol handshake
            "protocol" => new { protocol_version = 4, no_broadcast = false },

            // Player identification
            "player" => "Android",

            // Commands with no data
            "playerplay" or "playerpause" or "playerplaypause" or "playerstop" or
            "playernext" or "playerprevious" or "playerstatus" or "playeroutput" or
            "nowplayingtrack" or "nowplayingdetails" or "nowplayingcover" or
            "nowplayinglyrics" or "pluginversion" or "ping" or "pong" or
            "verifyconnection" or "init" or "librarycovercachebuildstatus"
                => new { },

            // Default: empty object
            _ => new { }
        };
    }

    #endregion

    #region Connection Management

    private void UpdateConnectionState(bool connected)
    {
        _isConnected = connected;
        CanConnect = !connected;
        CanDisconnect = connected;
        CanSendCommands = connected && _handshakeCompleted;

        if (connected)
        {
            var clientType = ClientTypeItems[SelectedClientTypeIndex];
            var protocolVersion = _negotiatedProtocol ?? ProtocolVersionItems[SelectedProtocolVersionIndex];
            StatusText = $"Connected to {IpAddress}:{Port}\nClient: {clientType}, Protocol: {protocolVersion}";
            StatusColor = "Green";
        }
        else
        {
            StatusText = "Disconnected";
            StatusColor = "Red";
            _handshakeCompleted = false;
        }
    }

    private void Disconnect()
    {
        if (!_isConnected)
            return;

        _isConnected = false;

        try
        {
            _networkStream?.Close();
            _tcpClient?.Close();
        }
        catch
        {
            // Ignore errors during disconnect
        }
        finally
        {
            _networkStream = null;
            _tcpClient = null;
            _handshakeCompleted = false;
            _playerAcknowledged = false;
            _negotiatedProtocol = null;
            UpdateConnectionState(false);
        }
    }

    #endregion

    #region Protocol Communication

    private Task SendPlayerMessage()
    {
        var playerMessage = new
        {
            context = "player",
            data = _pendingClientType.ToString()
        };

        var jsonMessage = JsonConvert.SerializeObject(playerMessage);
        AddLogMessage($"player: {_pendingClientType}", LogMessageType.Sent);
        return SendMessage(jsonMessage);
    }

    private Task SendProtocolMessage()
    {
        // V2 predates the extended handshake object — data is a bare integer.
        // "2.1" is still protocol 2 on the wire, so use the same legacy shape.
        var versionInt = int.Parse(
            _pendingProtocolVersion!.Split('.')[0],
            CultureInfo.InvariantCulture);

        object protocolMessage = versionInt == 2
            ? (object)new { context = "protocol", data = versionInt }
            : new
            {
                context = "protocol",
                data = new { protocol_version = versionInt, no_broadcast = NoBroadcast }
            };

        var jsonMessage = JsonConvert.SerializeObject(protocolMessage);
        AddLogMessage($"protocol: v{_pendingProtocolVersion}, no_broadcast={NoBroadcast}", LogMessageType.Sent);
        return SendMessage(jsonMessage);
    }

    private async Task SendMessage(string message)
    {
        if (!_isConnected || _networkStream == null)
        {
            AddLogMessage("Cannot send - not connected", LogMessageType.Warning);
            return;
        }

        try
        {
            var data = Encoding.UTF8.GetBytes(message + ProtocolConstants.MessageTerminator);
            await _networkStream.WriteAsync(data);
            await _networkStream.FlushAsync();
        }
        catch (Exception ex)
        {
            AddLogMessage($"Send failed: {ex.Message}", LogMessageType.Error);
            Disconnect();
        }
    }

    private async Task ListenForMessages()
    {
        var buffer = new byte[4096];
        var messageBuilder = new StringBuilder();

        try
        {
            while (_isConnected && _networkStream != null)
            {
                var bytesRead = await _networkStream.ReadAsync(buffer);

                if (bytesRead == 0)
                {
                    AddLogMessage("Server closed connection", LogMessageType.Warning);
                    break;
                }

                var data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                messageBuilder.Append(data);

                // Split on CRLF (the protocol terminator)
                var content = messageBuilder.ToString();
                var messages = content.Split(["\r\n"], StringSplitOptions.None);
                messageBuilder.Clear();

                for (var i = 0; i < messages.Length - 1; i++)
                {
                    var msg = messages[i].Trim();
                    if (!string.IsNullOrWhiteSpace(msg))
                        ProcessReceivedMessage(msg);
                }

                if (messages.Length > 0 && !string.IsNullOrEmpty(messages[^1]))
                    messageBuilder.Append(messages[^1]);
            }
        }
        catch (Exception ex)
        {
            AddLogMessage($"Listen error: {ex.Message}", LogMessageType.Error);
            await Dispatcher.UIThread.InvokeAsync(Disconnect);
        }

        AddLogMessage("Listener stopped", LogMessageType.Info);
    }

    private void ProcessReceivedMessage(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var json = JToken.Parse(message);
                var context = json["context"]?.ToString() ?? "unknown";

                // Handle ping messages automatically
                if (context == "ping")
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var pongMessage = new { context = "pong", data = "" };
                            await SendMessage(JsonConvert.SerializeObject(pongMessage));
                            AddLogMessage("pong", LogMessageType.Sent);
                        }
                        catch (Exception ex)
                        {
                            AddLogMessage($"Pong error: {ex.Message}", LogMessageType.Error);
                        }
                    });
                }

                // Handle handshake responses
                if (!_handshakeCompleted)
                {
                    if (context == "player" && !_playerAcknowledged)
                    {
                        _playerAcknowledged = true;
                        AddLogMessage("Player acknowledged", LogMessageType.Success);
                        _ = Task.Run(async () =>
                        {
                            try
                            { await SendProtocolMessage(); }
                            catch (Exception ex) { AddLogMessage($"Protocol send error: {ex.Message}", LogMessageType.Error); }
                        });
                    }
                    else if (context == "protocol" && _playerAcknowledged)
                    {
                        var data = json["data"]?.ToString();
                        if (!string.IsNullOrEmpty(data))
                        {
                            _handshakeCompleted = true;
                            _negotiatedProtocol = data;
                            AddLogMessage($"Handshake complete - Protocol {_negotiatedProtocol}", LogMessageType.Success);

                            Dispatcher.UIThread.Post(() =>
                            {
                                StatusText = $"Connected\nProtocol: {_negotiatedProtocol}";
                                StatusColor = "Green";
                                CanSendCommands = true;
                            });

                            _ = Task.Run(SendInitialRequests);
                        }
                    }
                }

                // Handle cover messages
                if (context == "nowplayingcover" || context == "libraryalbumcover")
                    ProcessCoverMessage(json);

                // Handle lyrics messages
                if (context == "nowplayinglyrics")
                    ProcessLyricsMessage(json);

                // Log received message - just show context, double-click for full JSON
                var originalFormatted = json.ToString(Formatting.Indented);
                AddLogMessage(context, LogMessageType.Received, originalFormatted);
            }
            catch (JsonReaderException)
            {
                if (!_handshakeCompleted &&
                    (message.Contains("ok", StringComparison.OrdinalIgnoreCase) ||
                     message.Contains("success", StringComparison.OrdinalIgnoreCase)))
                {
                    _handshakeCompleted = true;
                    _negotiatedProtocol = ProtocolVersionItems[SelectedProtocolVersionIndex];
                    AddLogMessage("Handshake acknowledged", LogMessageType.Success);
                }

                AddLogMessage($"NON-JSON: {message}", LogMessageType.Received);
            }
        });
    }

    private async Task SendInitialRequests()
    {
        try
        {
            await SendMessage(JsonConvert.SerializeObject(new { context = "verifyconnection", data = new { } }));
            await Task.Delay(100);

            await SendMessage(JsonConvert.SerializeObject(new { context = "player", data = new { } }));
            await Task.Delay(100);

            await SendMessage(JsonConvert.SerializeObject(new { context = "playervolume", data = new { } }));
        }
        catch (Exception ex)
        {
            AddLogMessage($"Initial request error: {ex.Message}", LogMessageType.Error);
        }
    }

    #endregion

    #region Cover and Lyrics Processing

    private void ProcessCoverMessage(JToken json)
    {
        try
        {
            var data = json["data"];
            if (data == null)
                return;

            AddLogMessage($"Cover data type: {data.Type}", LogMessageType.Info);

            if (data.Type == JTokenType.Object)
            {
                var coverBase64 = data["cover"]?.ToString();
                var statusValue = data["status"];
                var status = statusValue?.ToString() ?? "Unknown";

                var isSuccessStatus = status == "200" ||
                    (statusValue?.Type == JTokenType.Integer && statusValue.Value<int>() == 200);

                if (!string.IsNullOrEmpty(coverBase64) && isSuccessStatus)
                    DisplayCoverImage(coverBase64);
                else if (status == "1" || (statusValue?.Type == JTokenType.Integer && statusValue.Value<int>() == 1))
                    UpdateCoverStatus("Cover ready but not included", false);
                else if (status == "404" || (statusValue?.Type == JTokenType.Integer && statusValue.Value<int>() == 404))
                    UpdateCoverStatus("No cover available", false);
                else
                    UpdateCoverStatus($"Cover status: {status}", false);
            }
            else if (data.Type == JTokenType.String)
            {
                var base64String = data.ToString();
                if (!string.IsNullOrEmpty(base64String) && IsLikelyBase64(base64String))
                    DisplayCoverImage(base64String);
                else
                    UpdateCoverStatus($"Unexpected string data (length: {base64String?.Length})");
            }
            else
            {
                UpdateCoverStatus($"Unexpected data type: {data.Type}");
            }
        }
        catch (Exception ex)
        {
            AddLogMessage($"Error processing cover: {ex.Message}", LogMessageType.Error);
        }
    }

    private void ProcessLyricsMessage(JToken json)
    {
        try
        {
            var data = json["data"];
            if (data == null)
                return;

            AddLogMessage($"Lyrics data type: {data.Type}", LogMessageType.Info);

            if (data.Type == JTokenType.String)
            {
                var lyricsText = data.ToString();
                DisplayLyrics(lyricsText ?? "");
            }
            else if (data.Type == JTokenType.Object)
            {
                var lyricsText = data["lyrics"]?.ToString() ?? data["text"]?.ToString() ?? data.ToString();
                DisplayLyrics(lyricsText ?? "");
            }
            else
            {
                LyricsStatus = $"Unexpected data type: {data.Type}";
                LyricsStatusColor = "Red";
            }
        }
        catch (Exception ex)
        {
            AddLogMessage($"Error processing lyrics: {ex.Message}", LogMessageType.Error);
        }
    }

    private void DisplayCoverImage(string base64Data)
    {
        try
        {
            AddLogMessage($"Decoding base64 image (length: {base64Data.Length})", LogMessageType.Info);

            var imageBytes = Convert.FromBase64String(base64Data);
            AddLogMessage($"Base64 decoded - {imageBytes.Length:N0} bytes", LogMessageType.Success);

            using var ms = new MemoryStream(imageBytes);
            var image = new Bitmap(ms);

            Dispatcher.UIThread.Post(() =>
            {
                CoverImage?.Dispose();
                CoverImage = image;
                CoverStatus = $"Cover received\nSize: {image.Size.Width}x{image.Size.Height}\nBytes: {imageBytes.Length:N0}";
                CoverStatusColor = "LimeGreen";
            });

            AddLogMessage("Cover image displayed", LogMessageType.Success);
        }
        catch (Exception ex)
        {
            UpdateCoverStatus($"Error displaying cover: {ex.Message}");
            AddLogMessage($"Error decoding cover image: {ex.Message}", LogMessageType.Error);
        }
    }

    private void UpdateCoverStatus(string status, bool isError = true)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CoverStatus = status;
            CoverStatusColor = isError ? "Red" : "Gray";
        });
    }

    private void DisplayLyrics(string lyricsText)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (string.IsNullOrWhiteSpace(lyricsText))
            {
                LyricsText = "";
                LyricsStatus = "No lyrics found";
                LyricsStatusColor = "#DCDCAA"; // Yellow/warning color
                AddLogMessage("No lyrics available for current track", LogMessageType.Warning);
            }
            else
            {
                LyricsText = lyricsText;
                LyricsStatus = $"Lyrics loaded - {lyricsText.Length} characters";
                LyricsStatusColor = "#4EC9B0"; // Teal/success color
                AddLogMessage("Lyrics displayed", LogMessageType.Success);
            }
        });
    }

    #endregion

    #region Utility Methods

    private void AddLogMessage(string message, LogMessageType type = LogMessageType.Info, string? originalJson = null)
    {
        var logMessage = new LogMessage(type, message, originalJson);

        Dispatcher.UIThread.Post(() =>
        {
            LogMessages.Add(logMessage);
            if (type == LogMessageType.Received)
            {
                ReceivedOnlyMessages.Add(logMessage);
            }
            OnPropertyChanged(nameof(CurrentMessages));
        });

        Console.WriteLine($"LOG: [{logMessage.TimestampText}] {logMessage.DirectionIndicator} {message}");
    }

    public static string? GetJsonForMessage(LogMessage? message)
    {
        return message?.OriginalJson;
    }

    private static object ParseDataValue(string dataType, string dataValue)
    {
        return dataType switch
        {
            "Empty" => new { },
            "String" => dataValue ?? "",
            "Boolean" => bool.TryParse(dataValue, out var boolResult)
                ? boolResult
                : throw new ArgumentException($"Invalid boolean value: {dataValue}. Use 'true' or 'false'."),
            "Number" => int.TryParse(dataValue, out var intResult)
                ? intResult
                : double.TryParse(dataValue, out var doubleResult)
                    ? doubleResult
                    : throw new ArgumentException($"Invalid number value: {dataValue}"),
            "JSON" => JsonConvert.DeserializeObject(dataValue)
                      ?? throw new ArgumentException("Invalid JSON"),
            _ => new { }
        };
    }

    private static JToken SanitizeJsonForLogging(JToken json)
    {
        try
        {
            var sanitized = json.DeepClone();

            if (sanitized["context"] != null)
            {
                var context = sanitized["context"]!.ToString();

                if ((context == "nowplayingcover" || context == "libraryalbumcover") &&
                    sanitized["data"] != null &&
                    sanitized["data"]!.Type == JTokenType.String)
                {
                    var dataStr = sanitized["data"]!.ToString();
                    if (dataStr.Length > 1000 && IsLikelyBase64(dataStr))
                        sanitized["data"] = "<image>";
                }
                else if (context == "nowplayinglyrics" && sanitized["data"] != null)
                {
                    if (sanitized["data"]!.Type == JTokenType.String)
                    {
                        var lyricsText = sanitized["data"]!.ToString();
                        if (!string.IsNullOrEmpty(lyricsText) && lyricsText.Length > 50)
                            sanitized["data"] = "<lyrics>";
                    }
                    else if (sanitized["data"]!.Type == JTokenType.Object)
                    {
                        var dataObj = (JObject)sanitized["data"]!;
                        if (dataObj["lyrics"] != null && dataObj["lyrics"]!.Type == JTokenType.String)
                        {
                            var lyricsText = dataObj["lyrics"]!.ToString();
                            if (!string.IsNullOrEmpty(lyricsText) && lyricsText.Length > 50)
                                dataObj["lyrics"] = "<lyrics>";
                        }
                    }
                }
            }

            ReplaceBase64InToken(sanitized);
            return sanitized;
        }
        catch
        {
            return json;
        }
    }

    private static void ReplaceBase64InToken(JToken token)
    {
        if (token.Type == JTokenType.Object)
        {
            var obj = (JObject)token;
            foreach (var property in obj.Properties().ToList())
            {
                if (property.Name.Contains("cover", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("artwork", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.Type == JTokenType.String)
                    {
                        var value = property.Value.ToString();
                        if (value.Length > 1000 && IsLikelyBase64(value))
                            property.Value = "<image>";
                    }
                    else if (property.Value.Type == JTokenType.Object)
                    {
                        ReplaceBase64InToken(property.Value);
                    }
                }
                else
                {
                    ReplaceBase64InToken(property.Value);
                }
            }
        }
        else if (token.Type == JTokenType.Array)
        {
            var array = (JArray)token;
            foreach (var item in array)
                ReplaceBase64InToken(item);
        }
    }

    private static bool IsLikelyBase64(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 100)
            return false;

        var base64Pattern = @"^[A-Za-z0-9+/]*={0,2}$";
        return Regex.IsMatch(value, base64Pattern);
    }

    private static List<ProtocolCommand> LoadProtocolCommands()
    {
        return
        [
            // Connection & System
            new ProtocolCommand("init", "Initialize Connection"),
            new ProtocolCommand("ping", "Connection Ping"),
            new ProtocolCommand("protocol", "Protocol Negotiation"),
            new ProtocolCommand("pluginversion", "Plugin Version"),
            new ProtocolCommand("verifyconnection", "Verify Connection"),

            // Player Control
            new ProtocolCommand("player", "Player Status"),
            new ProtocolCommand("playerstatus", "Get Player Status"),
            new ProtocolCommand("playerstate", "Get Player State"),
            new ProtocolCommand("playerplay", "Start Playback"),
            new ProtocolCommand("playerpause", "Pause Playback"),
            new ProtocolCommand("playerplaypause", "Toggle Play/Pause"),
            new ProtocolCommand("playernext", "Next Track"),
            new ProtocolCommand("playerprevious", "Previous Track"),
            new ProtocolCommand("playerstop", "Stop Playback"),
            new ProtocolCommand("playervolume", "Volume Control"),
            new ProtocolCommand("playermute", "Mute Toggle"),
            new ProtocolCommand("playershuffle", "Shuffle Control"),
            new ProtocolCommand("playerrepeat", "Repeat Control"),
            new ProtocolCommand("playerautodj", "Auto DJ Control"),

            // Audio Output
            new ProtocolCommand("playeroutput", "Get Output Devices"),
            new ProtocolCommand("playeroutputswitch", "Switch Output Device"),

            // Now Playing
            new ProtocolCommand("nowplayingtrack", "Current Track Info"),
            new ProtocolCommand("nowplayingcover", "Current Track Cover"),
            new ProtocolCommand("nowplayingposition", "Playback Position"),
            new ProtocolCommand("nowplayinglyrics", "Current Track Lyrics"),
            new ProtocolCommand("nowplayingrating", "Track Rating"),
            new ProtocolCommand("nowplayinglfmrating", "Last.fm Love Rating"),
            new ProtocolCommand("nowplayinglist", "Now Playing List"),
            new ProtocolCommand("nowplayingqueue", "Queue to Now Playing"),
            new ProtocolCommand("nowplayingdetails", "Detailed Track Info"),
            new ProtocolCommand("nowplayingtagchange", "Change Track Tags"),
            new ProtocolCommand("nowplayinglistplay", "Play from Now Playing"),
            new ProtocolCommand("nowplayinglistremove", "Remove from Now Playing"),
            new ProtocolCommand("nowplayinglistmove", "Move in Now Playing"),
            new ProtocolCommand("nowplayinglistsearch", "Search Now Playing"),

            // Library Browsing
            new ProtocolCommand("browsegenres", "Browse Genres"),
            new ProtocolCommand("browseartists", "Browse Artists"),
            new ProtocolCommand("browsealbums", "Browse Albums"),
            new ProtocolCommand("browsetracks", "Browse Tracks"),

            // Library Search
            new ProtocolCommand("librarysearchartist", "Search by Artist"),
            new ProtocolCommand("librarysearchalbum", "Search by Album"),
            new ProtocolCommand("librarysearchgenre", "Search by Genre"),
            new ProtocolCommand("librarysearchtitle", "Search by Title"),

            // Library Navigation
            new ProtocolCommand("libraryartistalbums", "Artist Albums"),
            new ProtocolCommand("librarygenreartists", "Genre Artists"),
            new ProtocolCommand("libraryalbumtracks", "Album Tracks"),
            new ProtocolCommand("libraryalbumcover", "Album Cover"),
            new ProtocolCommand("libraryplayall", "Play All Tracks"),
            new ProtocolCommand("librarycovercachebuildstatus", "Cover Cache Status"),

            // Library Queue
            new ProtocolCommand("libraryqueuegenre", "Queue Genre"),
            new ProtocolCommand("libraryqueueartist", "Queue Artist"),
            new ProtocolCommand("libraryqueuealbum", "Queue Album"),
            new ProtocolCommand("libraryqueuetrack", "Queue Track"),

            // Playlists
            new ProtocolCommand("playlistlist", "Playlist List"),
            new ProtocolCommand("playlistplay", "Play Playlist"),

            // Radio & Streaming
            new ProtocolCommand("radiostations", "Radio Stations"),

            // Last.fm & Scrobbling
            new ProtocolCommand("scrobbler", "Scrobbler Status")
        ];
    }

    #endregion

    public void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        Disconnect();
        _discoveryService.Dispose();
        CoverImage?.Dispose();
        GC.SuppressFinalize(this);
    }
}
