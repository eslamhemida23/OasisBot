using System;
using System.Net;
using System.Net.Sockets;
using RSBot.Core.Event;
using RSBot.Core.Extensions;
using RSBot.Core.Network.Protocol;

namespace RSBot.Core.Network;

public class Server : NetBase
{
    private bool _receivedInitialHandshake;
    private bool _receivedPostInitialHandshakePacket;

    /// <summary>
    ///     Gets or sets the ip.
    /// </summary>
    /// <value>
    ///     The ip.
    /// </value>
    public string IP { get; set; }

    /// <summary>
    ///     Gets or sets the port.
    /// </summary>
    /// <value>
    ///     The port.
    /// </value>
    public ushort Port { get; set; }

    /// <summary>
    ///     Connects the specified ip.
    /// </summary>
    /// <param name="ip">The ip.</param>
    /// <param name="port">The port.</param>
    public void Connect(string ip, ushort port)
    {
        IP = ip;
        Port = port;

        if (_socket != null)
            Disconnect();

        try
        {
            _protocol = new SecurityProtocol();
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _receivedInitialHandshake = false;
            _receivedPostInitialHandshakePacket = false;

            try
            {
                var bindIp = GlobalConfig.Get("RSBot.Network.BindIp", "0.0.0.0");
                if (!string.IsNullOrWhiteSpace(bindIp) && bindIp != "0.0.0.0")
                    _socket.Bind(new IPEndPoint(IPAddress.Parse(bindIp), 0));

                if (ProxyConfig.TryGetProxy(ip, port, out var proxyConfig))
                {
                    if (!_socket.ConnectViaProxy(proxyConfig).Result)
                    {
                        Log.Warn("Proxy receiving has timeout!");
                        Disconnect();
                        return;
                    }
                }
                else
                {
                    _socket.Connect(ip, port, 3000);
                }
            }
            catch (AggregateException error)
            {
                var cause = error.Flatten().GetBaseException();
                Log.Error($"Connection to {ip}:{port} through the configured SOCKS proxy failed: {cause.Message}");
                Disconnect();
                return;
            }
            catch (SocketProxyException error)
            {
                Log.Error($"SOCKS proxy connection to {ip}:{port} failed: {error.Message}");
                Disconnect();
                return;
            }
            catch (SocketException error)
            {
                Log.Error(
                    $"Could not establish a TCP connection to {ip}:{port}. " +
                    $"SocketError={error.SocketErrorCode}, NativeError={error.ErrorCode}: {error.Message}"
                );
                Disconnect();
                return;
            }

            StartNetWorker();

            EnablePacketDispatcher = true;

            _socket?.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, OnBeginReceiveCallback, null);

            OnConnected();

            EventManager.FireEvent("OnServerConnected");
            Log.Notify($"TCP connection established to game server endpoint {ip}:{port}; waiting for the Silkroad handshake.");
        }
        catch (Exception error)
        {
            Log.Error($"Unexpected error while connecting to game server endpoint {ip}:{port}: {error}");
            Disconnect();
        }
    }

    /// <summary>
    ///     Waits for data.
    /// </summary>
    /// <param name="result">The result.</param>
    private void OnBeginReceiveCallback(IAsyncResult ar)
    {
        if (IsClosing || !EnablePacketDispatcher)
            return;

        var receivedSize = 0;

        try
        {
            receivedSize = _socket.EndReceive(ar, out var error);
            var rawOpcode = receivedSize >= 4 ? $"0x{BitConverter.ToUInt16(_buffer, 2):X4}" : "n/a";

            if (receivedSize == 0 || error != SocketError.Success)
            {
                if (!IsClosing && EnablePacketDispatcher)
                {
                    var endpoint = _socket?.RemoteEndPoint?.ToString() ?? $"{IP}:{Port}";
                    if (_receivedInitialHandshake && !_receivedPostInitialHandshakePacket)
                    {
                        Log.Warn(
                            $"Remote game server {endpoint} closed the connection after the initial 0x5000 packet " +
                            "but before the handshake completed. This can indicate an IP/session limit or a server-side rejection."
                        );
                    }
                    else if (!_receivedInitialHandshake)
                    {
                        Log.Warn(
                            $"Remote game server {endpoint} closed the connection before sending the initial 0x5000 handshake packet."
                        );
                    }
                    else
                    {
                        Log.Warn(
                            $"Remote game server {endpoint} closed the connection after the initial handshake."
                        );
                    }
                }

                OnDisconnected();
                return;
            }

            if (!_receivedInitialHandshake && rawOpcode == "0x5000")
                _receivedInitialHandshake = true;
            else if (_receivedInitialHandshake)
                _receivedPostInitialHandshakePacket = true;

            _protocol.Recv(_buffer, 0, receivedSize);
            SignalPacketDispatcher();
        }
        catch (SocketException error)
        {
            if (!IsClosing && EnablePacketDispatcher)
            {
                Log.Warn(
                    $"Socket receive failed for game server endpoint {IP}:{Port}. " +
                    $"SocketError={error.SocketErrorCode}, NativeError={error.ErrorCode}: {error.Message}"
                );
                OnDisconnected();
            }
        }
        catch (HandshakeSecurityException error)
        {
            Log.Error("Could not complete the Silkroad security handshake: " + error.Message);
            Game.Start();
        }
        catch (Exception error)
        {
            if (!IsClosing && EnablePacketDispatcher)
            {
                Log.Error($"Unexpected receive error from game server endpoint {IP}:{Port}: {error}");
                OnDisconnected();
            }
        }
        finally
        {
            try
            {
                if (!IsClosing && EnablePacketDispatcher && receivedSize != 0 && _socket != null && _socket.Connected)
                    _socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, OnBeginReceiveCallback, null);
            }
            catch (Exception error)
            {
                if (!IsClosing && EnablePacketDispatcher)
                {
                    Log.Warn($"Could not continue receiving from game server endpoint {IP}:{Port}: {error.Message}");
                    OnDisconnected();
                }
            }
        }
    }

    /// <summary>
    ///     Sends the specified packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    public void Send(Packet packet)
    {
        OnPacketSent(packet);
        _protocol.Send(packet);
        SignalPacketDispatcher();
    }

    /// <summary>
    ///     Disconnects this instance.
    /// </summary>
    public void Disconnect()
    {
        StopNetWorker();

        try
        {
            if (_socket != null && _socket.Connected)
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }
        }
        catch { }
        finally
        {
            _socket = null;
            OnDisconnected();
            IsClosing = false;
        }
    }
}
