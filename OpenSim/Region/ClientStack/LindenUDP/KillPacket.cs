/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using OpenMetaverse.Packets;

namespace OpenSim.Region.ClientStack.LindenUDP
{
    /// <summary>
    /// When packetqueue dequeues this packet in the outgoing stream, it thread aborts
    /// Ensures that the thread abort happens from within the client thread
    /// regardless of where the close method is called
    /// </summary>
    class KillPacket : Packet
    {
        private Header header;

        public override int Length
        {
            get { return 0; }
        }

        public override void FromBytes(Header header, byte[] bytes, ref int i, ref int packetEnd, byte[] zeroBuffer)
        {
        }

        public override void FromBytes(byte[] bytes, ref int i, ref int packetEnd, byte[] zeroBuffer)
        {
        }

        public override Header Header { get { return header; } set { header = value; }}

        public override byte[] ToBytes()
        {
            return new byte[0];
        }

        public KillPacket()
        {
            Header = new LowHeader();
            Header.ID = 65531;
            Header.Reliable = true;
        }

        public override PacketType Type
        {
            get
            {
                return PacketType.UseCircuitCode;
            }
        }
    }
}
