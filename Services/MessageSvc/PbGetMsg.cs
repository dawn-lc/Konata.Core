﻿using System;
using System.Collections.Generic;

using Konata.Core.Events;
using Konata.Core.Events.Model;
using Konata.Core.Message;
using Konata.Core.Message.Model;
using Konata.Core.Packets;
using Konata.Core.Packets.Protobuf;
using Konata.Utils.IO;
using Konata.Utils.Protobuf;
using Konata.Utils.Protobuf.ProtoModel;
using Konata.Core.Attributes;

namespace Konata.Core.Services.MessageSvc
{
    [Service("MessageSvc.PbGetMsg", "Get message")]
    [EventSubscribe(typeof(PrivateMessageEvent))]
    [EventSubscribe(typeof(PrivateMessagePullEvent))]
    internal class PbGetMsg : IService
    {
        public bool Parse(SSOFrame input, BotKeyStore signInfo, out ProtocolEvent output)
        {
            var message = new PrivateMessageEvent();
            {
                var root = ProtoTreeRoot.Deserialize(input.Payload, true);
                {
                    // Get sync cookie 
                    message.SyncCookie = ((ProtoLengthDelimited)(ProtoTreeRoot)root.PathTo("1A")).Value;

                    var sourceRoot = (ProtoTreeRoot)root.PathTo("2A.22.0A");
                    {
                        message.FriendUin = (uint)sourceRoot.GetLeafVar("08");
                    }

                    var sliceInfoRoot = (ProtoTreeRoot)root.PathTo("2A.22.12");
                    {
                        message.SliceTotal = (uint)sliceInfoRoot.GetLeafVar("08");
                        message.SliceIndex = (uint)sliceInfoRoot.GetLeafVar("10");
                        message.SliceFlags = (uint)sliceInfoRoot.GetLeafVar("18");
                    }

                    var contentRoot = (ProtoTreeRoot)root.PathTo("2A.22.1A.0A");
                    {
                        var list = new MessageChain();

                        contentRoot.ForEach((_, __) =>
                        {
                            if (_ == "12")
                            {
                                ((ProtoTreeRoot)__).ForEach((key, value) =>
                                {
                                    BaseChain chain = null;
                                    try
                                    {
                                        switch (key)
                                        {
                                            case "0A":
                                                chain = ParsePlainText((ProtoTreeRoot)value);
                                                break;

                                            case "12":
                                                chain = ParseQFace((ProtoTreeRoot)value);
                                                break;

                                            case "22":
                                                chain = ParsePicture((ProtoTreeRoot)value);
                                                break;
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        Console.WriteLine(e.Message, e.StackTrace);
                                    }

                                    if (chain != null)
                                    {
                                        list.Add(chain);
                                    }
                                });
                            }
                        });

                        message.Message = list;
                        message.SessionSequence = input.Sequence;
                    }
                }
            }

            output = message;
            return true;
        }

        /// <summary>
        /// Process Picture chain
        /// </summary>
        /// <param name="tree"></param>
        /// <returns></returns>
        private BaseChain ParsePicture(ProtoTreeRoot tree)
        {
            // TODO: fix args
            return ImageChain.Create(
                tree.GetLeafString("D201"),
                ByteConverter.Hex(tree.GetLeafBytes("3A")),
                tree.GetLeafString("0A"), 0, 0, 0, ImageType.JPG);
        }

        /// <summary>
        /// Process Text
        /// </summary>
        /// <param name="tree"></param>
        /// <returns></returns>
        private BaseChain ParsePlainText(ProtoTreeRoot tree)
            => PlainTextChain.Create(tree.GetLeafString("0A"));

        /// <summary>
        /// Process QFace chain
        /// </summary>
        /// <param name="tree"></param>
        /// <returns></returns>
        private BaseChain ParseQFace(ProtoTreeRoot tree)
            => QFaceChain.Create((uint)tree.GetLeafVar("08"));

        public bool Build(Sequence sequence, PrivateMessagePullEvent input,
            BotKeyStore signInfo, BotDevice device, out int newSequence, out byte[] output)
        {
            output = null;
            newSequence = sequence.NewSequence;

            var pullRequest = new PrivateMsgPullRequest(input.SyncCookie);

            if (SSOFrame.Create("MessageSvc.PbGetMsg", PacketType.TypeB,
                newSequence, sequence.Session, ProtoTreeRoot.Serialize(pullRequest), out var ssoFrame))
            {
                if (ServiceMessage.Create(ssoFrame, AuthFlag.D2Authentication,
                    signInfo.Account.Uin, signInfo.Session.D2Token, signInfo.Session.D2Key, out var toService))
                {
                    return ServiceMessage.Build(toService, device, out output);
                }
            }

            return false;
        }

        public bool Build(Sequence sequence, ProtocolEvent input,
            BotKeyStore signInfo, BotDevice device, out int newSequence, out byte[] output)
            => Build(sequence, (PrivateMessagePullEvent)input, signInfo, device, out newSequence, out output);
    }
}
