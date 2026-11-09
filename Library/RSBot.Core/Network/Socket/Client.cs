using System;
using System.Net;
using System.Net.Sockets;
using RSBot.Core.Network.Protocol;

namespace RSBot.Core.Network;

public class Client() : NetBase(isClient: true)
{
    /// <summary>
    ///     Gets or sets the listener.
    /// </summary>
    /// <value>
    ///     The listener.
    /// </value>
    public Socket _listener;

    internal ushort BoundPort => (ushort)((IPEndPoint)_listener.LocalEndPoint).Port;

    /// <summary>
    ///     Listens the specified port.
    /// </summary>
    /// <param name="port">The port.</param>
    public void Listen(ushort port)
    {
        try
        {
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
            _listener.Listen(1);
            StartNetWorker();
            _listener.BeginAccept(OnClientConnect, _listener);
        }
        catch
        {
            StopNetWorker();
            _listener?.Dispose();
            _listener = null;
            throw;
        }
    }

    /// <summary>
    ///     Listens this instance.
    /// </summary>
    private void Listen()
    {
        try
        {
            EnablePacketDispatcher = false;

            var previousSocket = _socket;
            _socket = null;

            if (previousSocket != null)
            {
                try
                {
                    if (previousSocket.Connected)
                        previousSocket.Shutdown(SocketShutdown.Both);
                }
                catch { }

                previousSocket.Close();
            }

            var listener = _listener;
            if (!IsClosing && listener != null)
                listener.BeginAccept(OnClientConnect, listener);
        }
        catch (Exception error)
        {
            if (!IsClosing)
                Log.Warn("[Network] Could not resume the local Silkroad client listener: " + error.Message);
        }
    }

    /// <summary>
    ///     Shutdowns this instance.
    /// </summary>
    public void Shutdown()
    {
        try
        {
            StopNetWorker();

            //Close Socket
            if (_socket != null)
            {
                if (_socket.Connected)
                    _socket.Shutdown(SocketShutdown.Both);

                _socket.Close();
                _socket = null;
            }

            //Close listener
            if (_listener != null)
            {
                _listener.Close();
                _listener = null;
            }

            _protocol = null;
        }
        catch { }
    }

    /// <summary>
    ///     Called when [client connect].
    /// </summary>
    /// <param name="ar">The ar.</param>
    private void OnClientConnect(IAsyncResult ar)
    {
        var listener = ar.AsyncState as Socket;
        Socket acceptedSocket = null;

        try
        {
            if (IsClosing || listener == null || !ReferenceEquals(listener, _listener))
                return;

            acceptedSocket = listener.EndAccept(ar);

            if (IsClosing || !ReferenceEquals(listener, _listener))
            {
                acceptedSocket.Close();
                return;
            }

            _socket = acceptedSocket;
            _protocol = new SecurityProtocol();
            _protocol.GenerateSecurity(true, true, true);

            EnablePacketDispatcher = true;
            acceptedSocket.BeginReceive(
                _buffer,
                0,
                _buffer.Length,
                SocketFlags.None,
                OnBeginReceiveCallback,
                acceptedSocket
            );

            OnConnected();
        }
        catch (Exception error)
        {
            try { acceptedSocket?.Close(); } catch { }

            if (!IsClosing)
            {
                Log.Warn("[Network] Local Silkroad client accept failed: " + error.Message);
                OnDisconnected();
                Listen();
            }
        }
    }

    /// <summary>
    ///     Waits for data.
    /// </summary>
    /// <param name="ar">The ar.</param>
    private void OnBeginReceiveCallback(IAsyncResult ar)
    {
        var socket = ar.AsyncState as Socket;
        if (socket == null)
            return;

        if (IsClosing || !ReferenceEquals(socket, _socket))
        {
            try { socket.EndReceive(ar); } catch { }
            return;
        }

        var receivedSize = 0;

        try
        {
            receivedSize = socket.EndReceive(ar, out var error);
            if (receivedSize == 0 || error != SocketError.Success)
            {
                if (!IsClosing && ReferenceEquals(socket, _socket))
                {
                    Log.Warn("[Network] Silkroad client disconnected from the local proxy.");
                    OnDisconnected();
                    Listen();
                }
                return;
            }

            _protocol.Recv(_buffer, 0, receivedSize);
            SignalPacketDispatcher();
        }
        catch (SocketException error)
        {
            if (!IsClosing && ReferenceEquals(socket, _socket))
            {
                Log.Warn(
                    "[Network] Local Silkroad client socket receive failed. " +
                    $"SocketError={error.SocketErrorCode}, NativeError={error.ErrorCode}: {error.Message}"
                );
                OnDisconnected();
                Listen();
            }
        }
        catch (HandshakeSecurityException error)
        {
            if (!IsClosing && ReferenceEquals(socket, _socket))
            {
                Log.Error("[Fatal]: Could not handshake the client: " + error.Message);
                Kernel.Proxy?.Shutdown();
            }
        }
        catch (ObjectDisposedException error)
        {
            if (!IsClosing && ReferenceEquals(socket, _socket))
            {
                Log.Warn("[Network] Local Silkroad client socket was disposed unexpectedly: " + error.Message);
                OnDisconnected();
                Listen();
            }
        }
        catch (Exception error)
        {
            if (!IsClosing && ReferenceEquals(socket, _socket))
            {
                Log.Error("[Network] Unexpected local client receive error: " + error);
                OnDisconnected();
                Listen();
            }
        }
        finally
        {
            try
            {
                if (
                    receivedSize != 0
                    && !IsClosing
                    && EnablePacketDispatcher
                    && ReferenceEquals(socket, _socket)
                    && socket.Connected
                )
                    socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, OnBeginReceiveCallback, socket);
            }
            catch (Exception error)
            {
                if (!IsClosing && ReferenceEquals(socket, _socket))
                {
                    Log.Warn("[Network] Could not continue receiving from the local Silkroad client: " + error.Message);
                    OnDisconnected();
                    Listen();
                }
            }
        }
    }
}
