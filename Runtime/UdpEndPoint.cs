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
using System.Net;
using System.Net.Sockets;

namespace Neutron.Core
{
    internal class UdpEndPoint : IPEndPoint
    {
        private SocketAddress socketAddress;
        internal UdpEndPoint(long address, int port) : base(address, port) => socketAddress = base.Serialize();
        internal UdpEndPoint(IPAddress address, int port) : base(address, port) => socketAddress = base.Serialize();

        [Obsolete("Use \"GetPort()\" instead!")] public new int Port => GetPort();
        [Obsolete("Use \"GetIPAddress()\" instead!")] public new IPAddress Address => new(GetIPAddress());

        public override AddressFamily AddressFamily => AddressFamily.InterNetwork;
        public override SocketAddress Serialize() => socketAddress;
        public override EndPoint Create(SocketAddress socketAddress)
        {
            if (socketAddress.Family != AddressFamily)
            {
                Logger.PrintError("Invalid address family");
                return default;
            }

            if (socketAddress.Size < 8)
            {
                Logger.PrintError("Error: SocketAddress.Size < 8");
                return default;
            }

            if (this.socketAddress != socketAddress)
            {
                this.socketAddress = socketAddress;

                unchecked
                {
                    this.socketAddress[0] += 1;
                    this.socketAddress[0] -= 1;
                }

                if (this.socketAddress.GetHashCode() == 0)
                {
                    Logger.PrintError("Error: SocketAddress.GetHashCode() == 0");
                    return default;
                }
            }

            return this;
        }

        internal long GetIPAddress()
        {
            switch (AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    {
                        long address = (
                                socketAddress[4] & 0x000000FF |
                                socketAddress[5] << 8 & 0x0000FF00 |
                                socketAddress[6] << 16 & 0x00FF0000 |
                                socketAddress[7] << 24
                                ) & 0x00000000FFFFFFFF;
                        return address;
                    }
                default:
                    return default;
            }
        }

        internal int GetPort()
        {
            switch (AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    {
                        int port = (
                                socketAddress[2] << 8 & 0x0000FF00 |
                                socketAddress[3]
                                ) & 0x0000FFFF;
                        return port;
                    }
                default:
                    return default;
            }
        }

        public override int GetHashCode() => socketAddress.GetHashCode();
        public override string ToString() => $"{NeutronHelper.ToAddress(GetIPAddress())}:{GetPort()}";
        public override bool Equals(object obj) => obj is UdpEndPoint other && GetIPAddress() == other.GetIPAddress() && GetPort() == other.GetPort();
    }
}