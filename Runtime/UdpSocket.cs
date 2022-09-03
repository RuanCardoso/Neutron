/*===========================================================
    Author: Ruan Cardoso
    -
    Country: Brazil(Brasil)
    -
    Contact: cardoso.ruan050322@gmail.com
    -
    Support: neutron050322@gmail.com
    -
    Unity Minor Version: 2021.3 LTS
    -
    License: Open Source (MIT)
    ===========================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ThreadPriority = System.Threading.ThreadPriority;

namespace Neutron.Core
{
    internal abstract class UdpSocket
    {
        internal class ChannelObject
        {
            internal uint sequence = 0;
            internal uint expectedSequence = 1;
            internal uint minAck = 1;
            internal uint maxAck = 1;
            internal readonly object sync_root = new();
            internal readonly HashSet<uint> acknowledgmentsReceived = new();
            internal readonly SortedDictionary<uint, ByteStream> dataBySequence = new();
            internal readonly ConcurrentDictionary<uint, ByteStream> relayMessages = new();
        }

        protected abstract void OnMessage(ByteStream recvStream, Channel channel, Target target, MessageType messageType, UdpEndPoint remoteEndPoint);
        internal abstract UdpClient GetClient(UdpEndPoint remoteEndPoint);

        protected abstract bool IsServer { get; }
        protected abstract string Name { get; }

        internal Socket globalSocket;
        internal readonly CancellationTokenSource cancellationTokenSource = new();

        internal readonly ChannelObject reliableChannel = new();
        internal readonly ChannelObject reliableAndOrderlyChannel = new();

        internal void Bind(UdpEndPoint localEndPoint)
        {
            globalSocket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                globalSocket.Bind(localEndPoint);
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    Logger.PrintWarning($"The {Name} not binded to {localEndPoint} because it is already in use.");
            }
            StartReadingData();
        }

        protected async void SendReliableMessages(UdpEndPoint remoteEndPoint)
        {
            await Task.Run(async () =>
            {
                Channel[] channels = {
                    Channel.Reliable,
                    Channel.ReliableAndOrderly
                };

                List<uint> toRemove = new List<uint>();
                while (!cancellationTokenSource.IsCancellationRequested)
                {
                    await Task.Delay(15);
                    for (int i = 0; i < channels.Length; i++)
                    {
                        ChannelObject channelObject = GetChannelObject(channels[i]);
                        foreach (var (seq, _stream_) in channelObject.relayMessages)
                        {
                            if (!_stream_.isRelease)
                            {
                                if (DateTime.UtcNow.Subtract(_stream_.LastWriteTime).TotalSeconds > 0.100d)
                                {
                                    _stream_.Position = 0;
                                    _stream_.SetLastWriteTime();
                                    Send(_stream_, remoteEndPoint);
                                }
                            }
                            else toRemove.Add(seq);
                        }

                        for (int i1 = 0; i1 < toRemove.Count; i1++)
                        {
                            uint seq = toRemove[i1];
                            if (channelObject.relayMessages.TryRemove(seq, out ByteStream _stream_))
                            {
                                _stream_.isRelease = false;
                                _stream_.Release();
                            }
                        }

                        toRemove.Clear();
                    }
                }
            }, cancellationTokenSource.Token);
        }

        protected int SendUnreliable(ByteStream byteStream, UdpEndPoint remoteEndPoint, Target target = Target.Me)
        {
            ByteStream poolStream = ByteStream.Get();
            poolStream.Write((byte)((byte)Channel.Unreliable | (byte)target << 2));
            poolStream.Write(byteStream);
            int length = Send(poolStream, remoteEndPoint);
            poolStream.Release();
            return length;
        }

        protected int SendReliable(ByteStream byteStream, UdpEndPoint remoteEndPoint, Channel channel = Channel.Reliable, Target target = Target.Me)
        {
            uint _sequence_ = 0;
            if (IsServer)
                Logger.PrintError("The server can't send data directly to a client, use the client object(UdpClient) to send data.");
            else
            {
                ChannelObject channelObject = GetChannelObject(channel);
                ByteStream poolStream = ByteStream.Get();
                poolStream.Write((byte)((byte)channel | (byte)target << 2));
                lock (channelObject.sync_root) poolStream.Write(_sequence_ = ++channelObject.sequence);
                poolStream.Write(byteStream);
                ByteStream relayStream = ByteStream.Get();
                relayStream.Write(poolStream);
                relayStream.SetLastWriteTime();
                channelObject.relayMessages.TryAdd(_sequence_, relayStream);
                int length = Send(poolStream, remoteEndPoint);
                poolStream.Release();
                return length;
            }
            return 0;
        }

        protected int Send(ByteStream byteStream, UdpEndPoint remoteEndPoint, int offset = 0)
        {
            try
            {
                if (byteStream.isRawBytes)
                {
                    uint _sequence_ = 0;
                    byteStream.Position = 0;
                    Channel channel = (Channel)(byteStream.ReadByte() & 0x3);
                    switch (channel)
                    {
                        case Channel.Reliable:
                        case Channel.ReliableAndOrderly:
                            {
                                ChannelObject channelObject = GetChannelObject(channel);
                                byte[] buffer = byteStream.Buffer;
                                lock (channelObject.sync_root) _sequence_ = ++channelObject.sequence;
                                buffer[++offset] = (byte)_sequence_;
                                buffer[++offset] = (byte)(_sequence_ >> 8);
                                buffer[++offset] = (byte)(_sequence_ >> 16);
                                buffer[++offset] = (byte)(_sequence_ >> 24);
                                offset = 0;

                                ByteStream relayStream = ByteStream.Get();
                                relayStream.Write(byteStream);
                                relayStream.SetLastWriteTime();
                                channelObject.relayMessages.TryAdd(_sequence_, relayStream);
                                break;
                            }
                    }
                }

                int bytesWritten = byteStream.BytesWritten;
                int length = globalSocket.SendTo(byteStream.Buffer, offset, bytesWritten - offset, SocketFlags.None, remoteEndPoint);
                if (length != bytesWritten) Logger.PrintError($"{Name} - Send - Failed to send {bytesWritten} bytes to {remoteEndPoint}");
                return length;
            }
            catch (ObjectDisposedException) { return 0; }
        }

        private void StartReadingData()
        {
            new Thread(() =>
            {
                byte[] buffer = new byte[0x5DC];
                EndPoint endPoint = new UdpEndPoint(0, 0);
                while (!cancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        int length = globalSocket.ReceiveFrom(buffer, SocketFlags.None, ref endPoint);
                        if (length > 0)
                        {
                            UdpEndPoint remoteEndPoint = (UdpEndPoint)endPoint;
                            ByteStream recvStream = ByteStream.Get();
                            recvStream.Write(buffer, 0, length);
                            recvStream.Position = 0;
                            recvStream.isRawBytes = true;

                            #region Bit Mask
                            byte maskBit = recvStream.ReadByte();
                            Channel bitChannel = (Channel)(maskBit & 0x3);
                            Target bitTarget = (Target)((maskBit >> 2) & 0x3);
                            if ((byte)bitTarget > 0x3 || (byte)bitChannel > 0x3)
                            {
                                Logger.PrintError($"{Name} - StartReadingData - Invalid target -> {bitTarget} or channel -> {bitChannel}");
                                recvStream.Release();
                                continue;
                            }
                            #endregion

                            switch (bitChannel)
                            {
                                case Channel.Unreliable:
                                    {
                                        MessageType msgType = recvStream.ReadPacket();
                                        switch (msgType)
                                        {
                                            case MessageType.Acknowledgement:
                                                {
                                                    UdpClient client = GetClient(remoteEndPoint);
                                                    if (client != null)
                                                    {
                                                        Channel _channel_ = (Channel)recvStream.ReadByte();
                                                        ChannelObject channelObject = client.GetChannelObject(_channel_);
                                                        uint sequence = recvStream.ReadUInt();
                                                        if (channelObject.relayMessages.TryGetValue(sequence, out ByteStream stream))
                                                        {
                                                            if (!stream.isRelease) stream.isRelease = true;
                                                        }
                                                    }
                                                }
                                                break;
                                            default:
                                                OnMessage(recvStream, bitChannel, bitTarget, msgType, remoteEndPoint);
                                                break;
                                        }
                                    }
                                    break;
                                case Channel.Reliable:
                                case Channel.ReliableAndOrderly:
                                    {
                                        #region Send Acknowledgement
                                        uint ack = recvStream.ReadUInt();
                                        ByteStream ackStream = ByteStream.Get();
                                        ackStream.WritePacket(MessageType.Acknowledgement);
                                        ackStream.Write((byte)bitChannel);
                                        ackStream.Write(ack);
                                        SendUnreliable(ackStream, remoteEndPoint, Target.Me);
                                        ackStream.Release();
                                        #endregion

                                        MessageType msgType = recvStream.ReadPacket();
                                        switch (msgType)
                                        {
                                            case MessageType.Connect:
                                                {
                                                    UdpClient client = GetClient(remoteEndPoint);
                                                    if (client != null)
                                                    {
                                                        ChannelObject channelObject = client.GetChannelObject(bitChannel);
                                                        if (!channelObject.acknowledgmentsReceived.Add(ack))
                                                        {
                                                            recvStream.Release();
                                                            continue;
                                                        }
                                                        OnMessage(recvStream, bitChannel, bitTarget, msgType, remoteEndPoint);
                                                    }
                                                    else
                                                    {
                                                        OnMessage(recvStream, bitChannel, bitTarget, msgType, remoteEndPoint);
                                                        UdpClient _client_ = GetClient(remoteEndPoint);
                                                        if (_client_ != null) _client_.GetChannelObject(bitChannel).acknowledgmentsReceived.Add(ack);
                                                    }
                                                }
                                                break;
                                            default:
                                                {
                                                    UdpClient client = GetClient(remoteEndPoint);
                                                    if (client != null)
                                                    {
                                                        ChannelObject channelObject = client.GetChannelObject(bitChannel);
                                                        switch (bitChannel)
                                                        {
                                                            case Channel.Reliable:
                                                                if (!channelObject.acknowledgmentsReceived.Add(ack))
                                                                {
                                                                    recvStream.Release();
                                                                    continue;
                                                                }
                                                                else OnMessage(recvStream, bitChannel, bitTarget, msgType, remoteEndPoint);
                                                                break;
                                                            case Channel.ReliableAndOrderly:
                                                                {
                                                                    if (ack < channelObject.expectedSequence || !channelObject.acknowledgmentsReceived.Add(ack))
                                                                    {
                                                                        recvStream.Release();
                                                                        continue;
                                                                    }

                                                                    #region Write Sequenced Messages
                                                                    ByteStream data = ByteStream.Get();
                                                                    data.Write(recvStream.Buffer, 0, recvStream.BytesWritten);
                                                                    data.Position = recvStream.Position;
                                                                    recvStream.Release();
                                                                    data.isRawBytes = true;
                                                                    channelObject.dataBySequence.Add(ack, data);
                                                                    #endregion

                                                                    #region Put Data By Sequence
                                                                    channelObject.minAck = Math.Min(channelObject.minAck, ack);
                                                                    channelObject.maxAck = Math.Max(channelObject.maxAck, ack);
                                                                    if (channelObject.minAck == channelObject.expectedSequence)
                                                                    {
                                                                        uint range = channelObject.maxAck - (channelObject.minAck - 1);
                                                                        if (channelObject.acknowledgmentsReceived.Count == range)
                                                                        {
                                                                            foreach (var (_, dataBySequence) in channelObject.dataBySequence)
                                                                            {
                                                                                OnMessage(dataBySequence, bitChannel, bitTarget, msgType, remoteEndPoint);
                                                                                dataBySequence.Release();
                                                                            }

                                                                            channelObject.expectedSequence = channelObject.maxAck + 1;
                                                                            channelObject.minAck = channelObject.maxAck = channelObject.expectedSequence;
                                                                            channelObject.acknowledgmentsReceived.Clear();
                                                                            channelObject.dataBySequence.Clear();
                                                                        }
                                                                        else { /* Get missing messages  */}
                                                                    }
                                                                    else { /* Get missing messages  */}
                                                                }
                                                                #endregion
                                                                break;
                                                        }
                                                    }
                                                }
                                                break;
                                        }
                                    }
                                    break;
                                default:
                                    Logger.PrintError($"Unknown channel {bitChannel} received from {remoteEndPoint}");
                                    break;
                            }

                            if (bitChannel != Channel.ReliableAndOrderly)
                                recvStream.Release();
                        }
                        else
                            Logger.PrintError($"{Name} - Receive - Failed to receive {length} bytes from {endPoint}");
                    }
                    catch (ThreadAbortException) { continue; }
                    catch (ObjectDisposedException) { continue; }
                    catch (SocketException ex)
                    {
                        if (ex.ErrorCode == 10004)
                            break;

                        Logger.LogStacktrace(ex);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogStacktrace(ex);
                        continue;
                    }
                }
            })
            {
                Name = Name,
                IsBackground = true,
                Priority = ThreadPriority.Highest
            }.Start();
        }

        private ChannelObject GetChannelObject(Channel channel)
        {
            switch (channel)
            {
                case Channel.Reliable:
                    return reliableChannel;
                case Channel.ReliableAndOrderly:
                    return reliableAndOrderlyChannel;
                default:
                    return default;
            }
        }

        internal virtual void Close(bool fromServer = false)
        {
            try
            {
                cancellationTokenSource.Cancel();
                if (!fromServer)
                    globalSocket.Close();
            }
            catch { }
            finally
            {
                cancellationTokenSource.Dispose();
                if (globalSocket != null && !fromServer)
                    globalSocket.Dispose();
            }
        }
    }
}