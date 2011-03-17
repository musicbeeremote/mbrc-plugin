using System;
using System.Threading;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Configuration;
using System.Text;

namespace MusicBeePlugin
{
    class SocketServer
    {
        private static bool _continueListening = true;
        private TcpListener tcpListener;
        private Thread listenThread;

        //Currently played song

        private static string _artist;
        private static string _album;
        private static string _song;
        private static string _year;
        private static string _imageData;
        private static bool _songChanged;

        public bool continueListening
        {
            get
            {
                return _continueListening;
            }
            set
            {
                _continueListening = value;
            }
        }

        public string artist
        {
            get
            {
                return _artist;
            }
            set
            {
                _artist = value;
            }
        }

        public string album
        {
            get
            {
                return _album;
            }
            set
            {
                _album = value;
            }
        }

        public string song
        {
            get
            {
                return _song;
            }
            set
            {
                _song = value;
            }
        }

        public string year
        {
            get
            {
                return _year;
            }
            set
            {
                _year = value;
            }
        }

        public bool songChanged
        {
            get
            {
                return _songChanged;
            }
            set
            {
                _songChanged = value;
            }
        }

        public string imageData
        {
           get
           {
               return _imageData;
           }
           set
           {
              _imageData = value;
           }
        }
        //currently played song info end

        public SocketServer()
        {
            this.tcpListener = new TcpListener(IPAddress.Any, 9741);
            this.listenThread = new Thread(new ThreadStart(ListenForClients));
            this.listenThread.Start();
        }
        private void ListenForClients()
        {
            this.tcpListener.Start();
            while (true)
            {
                TcpClient client = this.tcpListener.AcceptTcpClient();
                Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClientComm));
                clientThread.Start(client);
            }
        }
        public static void HandleClientComm(object client)
        {
            TcpClient tcpClient = (TcpClient)client;
            NetworkStream clientStream = tcpClient.GetStream();

            UTF8Encoding encoder = new UTF8Encoding();
            string commandToSend;
            commandToSend = "info::" + _artist + "~" + _song + "~" + _album + "~" + _year;
            byte[] sendBuffer = encoder.GetBytes(commandToSend);
            byte[] receiveBuffer = new byte[4096];
            //int bytesRead;
            while (true)
            {
                try
                {
                    if (_songChanged == true)
                    {
                        commandToSend = "info::" + _artist + "~" + _song + "~" + _album + "~" + _year;
                        sendBuffer = encoder.GetBytes(commandToSend);
                        clientStream.Write(sendBuffer, 0, sendBuffer.Length);
                        clientStream.Flush();

                        sendBuffer = encoder.GetBytes("image::" + _imageData);
                        clientStream.Write(sendBuffer, 0, sendBuffer.Length);
                        clientStream.Flush();
                        _songChanged = false;
                    }
                    
                    //bytesRead = clientStream.Read(receiveBuffer, 0, 4096);
                    //string reply = encoder.GetString(receiveBuffer);
                    //if (reply == "OK")
                    //    _songChanged = false;
                }
                catch
                {
                    break;
                }
            }
            tcpClient.Close();
        }
    }
}

