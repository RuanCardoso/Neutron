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

using Neutron.Core;
using Neutron.Resolvers;
using System;
using UnityEngine;
using Logger = Neutron.Core.Logger;

namespace Neutron.Tests
{
    [AddComponentMenu("")]
    public class GlobalMessagesTests : MonoBehaviour
    {
        private MessageStream netMoveStream;

        private void Awake()
        {
            NeutronNetwork.AddResolver(NeutronTestsResolver.Instance);
            //-------------------------------------------------------
            netMoveStream = new();
        }

        private void Start()
        {
            NeutronNetwork.AddHandler<NetMove>(OnNetMove);
        }

        private void OnNetMove(ReadOnlyMemory<byte> data, ushort playerId, bool isServer, RemoteStats stats)
        {
            NetMove netMove = data.GetMessage<NetMove>();
            Logger.PrintError("AHHHH: " + isServer);
        }

        private double lastSyncTime;
        private void Update()
        {
            if (NeutronNetwork.Interval(ref lastSyncTime, 1, true))
            {
                NetMove netMove = new(transform.position, transform.rotation, NeutronTime.Time);
                netMove.SendMessage(netMoveStream, false);
            }
        }
    }
}