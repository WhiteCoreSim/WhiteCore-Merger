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

using System;
using System.Data;
using NHibernate;
using NHibernate.SqlTypes;
using NHibernate.UserTypes;

namespace OpenSim.Data.NHibernate
{
    [Serializable]
    public class SByteType: IUserType
    {
        public object Assemble(object cached, object owner)
        {
            return cached;
        }

        bool IUserType.Equals(object sbyte1, object sbyte2)
        {
            return sbyte1.Equals(sbyte2);
        }

        public object DeepCopy(object sbyte1)
        {
            return sbyte1;
        }

        public object Disassemble(object sbyte1)
        {
            return sbyte1;
        }

        public int GetHashCode(object sbyte1)
        {
            return (sbyte1 == null) ? 0 : sbyte1.GetHashCode();
        }

        public bool IsMutable
        {
            get { return false; }
        }

        public object NullSafeGet(IDataReader rs, string[] names, object owner)
        {
            object sbyte1 = null;

            int ord = rs.GetOrdinal(names[0]);
            if (!rs.IsDBNull(ord))
            {
                sbyte1 = Convert.ToSByte(rs.GetInt16(ord));
            }
            return sbyte1;
        }

        public void NullSafeSet(IDbCommand cmd, object obj, int index)
        {
            sbyte b = (sbyte)obj;
            ((IDataParameter)cmd.Parameters[index]).Value = Convert.ToInt16(b);
        }

        public object Replace(object original, object target, object owner)
        {
            return original;
        }

        public Type ReturnedType
        {
            get { return typeof(sbyte); }
        }

        public SqlType[] SqlTypes
        {
            get { return new SqlType [] { NHibernateUtil.Byte.SqlType }; }
        }
    }
}
