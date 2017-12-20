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
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using log4net;
using Mono.Data.SqliteClient;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;

namespace OpenSim.Data.SQLite
{
    public class SQLiteEstateStore : IEstateDataStore
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private SqliteConnection m_connection;
        private string m_connectionString;

        private FieldInfo[] m_Fields;
        private Dictionary<string, FieldInfo> m_FieldMap =
                new Dictionary<string, FieldInfo>();

        public void Initialize(string connectionString)
        {
            m_connectionString = connectionString;

            m_log.Info("[ESTATE DB]: Sqlite - connecting: "+m_connectionString);

            m_connection = new SqliteConnection(m_connectionString);
            m_connection.Open();

            Assembly assem = GetType().Assembly;
            Migration m = new Migration(m_connection, assem, "EstateStore");
            m.Update();

            m_connection.Close();
            m_connection.Open();

            Type t = typeof(EstateSettings);
            m_Fields = t.GetFields(BindingFlags.NonPublic |
                                   BindingFlags.Instance |
                                   BindingFlags.DeclaredOnly);

            foreach (FieldInfo f in m_Fields)
                if (f.Name.Substring(0, 2) == "m_")
                    m_FieldMap[f.Name.Substring(2)] = f;
        }

        private string[] FieldList
        {
            get { return new List<string>(m_FieldMap.Keys).ToArray(); }
        }

        public EstateSettings LoadEstateSettings(UUID regionID)
        {
            EstateSettings es = new EstateSettings();
            es.OnSave += StoreEstateSettings;

            string sql = "select estate_settings."+String.Join(",estate_settings.", FieldList)+" from estate_map left join estate_settings on estate_map.EstateID = estate_settings.EstateID where estate_settings.EstateID is not null and RegionID = :RegionID";

            SqliteCommand cmd = (SqliteCommand)m_connection.CreateCommand();

            cmd.CommandText = sql;
            cmd.Parameters.Add(":RegionID", regionID.ToString());

            IDataReader r = cmd.ExecuteReader();

            if (r.Read())
            {
                foreach (string name in FieldList)
                {
                    if (m_FieldMap[name].GetValue(es) is bool)
                    {
                        int v = Convert.ToInt32(r[name]);
                        if (v != 0)
                            m_FieldMap[name].SetValue(es, true);
                        else
                            m_FieldMap[name].SetValue(es, false);
                    }
                    else if (m_FieldMap[name].GetValue(es) is UUID)
                    {
                        UUID uuid = UUID.Zero;

                        UUID.TryParse(r[name].ToString(), out uuid);
                        m_FieldMap[name].SetValue(es, uuid);
                    }
                    else
                    {
                        m_FieldMap[name].SetValue(es, Convert.ChangeType(r[name], m_FieldMap[name].FieldType));
                    }
                }
                r.Close();
            }
            else
            {
                // Migration case
                //
                r.Close();

                List<string> names = new List<string>(FieldList);

                names.Remove("EstateID");

                sql = "insert into estate_settings ("+String.Join(",", names.ToArray())+") values ( :"+String.Join(", :", names.ToArray())+")";

                cmd.CommandText = sql;
                cmd.Parameters.Clear();

                foreach (string name in FieldList)
                {
                    if (m_FieldMap[name].GetValue(es) is bool)
                    {
                        if ((bool)m_FieldMap[name].GetValue(es))
                            cmd.Parameters.Add(":"+name, "1");
                        else
                            cmd.Parameters.Add(":"+name, "0");
                    }
                    else
                    {
                        cmd.Parameters.Add(":"+name, m_FieldMap[name].GetValue(es).ToString());
                    }
                }

                cmd.ExecuteNonQuery();

                cmd.CommandText = "select LAST_INSERT_ROWID() as id";
                cmd.Parameters.Clear();

                r = cmd.ExecuteReader();

                r.Read();

                es.EstateID = Convert.ToUInt32(r["id"]);

                r.Close();

                cmd.CommandText = "insert into estate_map values (:RegionID, :EstateID)";
                cmd.Parameters.Add(":RegionID", regionID.ToString());
                cmd.Parameters.Add(":EstateID", es.EstateID.ToString());

                // This will throw on dupe key
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (Exception)
                {
                }

                // Munge and transfer the ban list
                //
                cmd.Parameters.Clear();
                cmd.CommandText = "insert into estateban select "+es.EstateID.ToString()+", bannedUUID, bannedIp, bannedIpHostMask, '' from regionban where regionban.regionUUID = :UUID";
                cmd.Parameters.Add(":UUID", regionID.ToString());

                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (Exception)
                {
                }

                es.Save();
            }

            LoadBanList(es);

            es.EstateManagers = LoadUUIDList(es.EstateID, "estate_managers");
            es.EstateAccess = LoadUUIDList(es.EstateID, "estate_users");
            es.EstateGroups = LoadUUIDList(es.EstateID, "estate_groups");
            return es;
        }

        public void StoreEstateSettings(EstateSettings es)
        {
            List<string> fields = new List<string>(FieldList);
            fields.Remove("EstateID");

            List<string> terms = new List<string>();

            foreach (string f in fields)
                terms.Add(f+" = :"+f);

            string sql = "update estate_settings set "+String.Join(", ", terms.ToArray())+" where EstateID = :EstateID";

            SqliteCommand cmd = (SqliteCommand)m_connection.CreateCommand();

            cmd.CommandText = sql;

            foreach (string name in FieldList)
            {
                if (m_FieldMap[name].GetValue(es) is bool)
                {
                    if ((bool)m_FieldMap[name].GetValue(es))
                        cmd.Parameters.Add(":"+name, "1");
                    else
                        cmd.Parameters.Add(":"+name, "0");
                }
                else
                {
                    cmd.Parameters.Add(":"+name, m_FieldMap[name].GetValue(es).ToString());
                }
            }

            cmd.ExecuteNonQuery();

            SaveBanList(es);
            SaveUUIDList(es.EstateID, "estate_managers", es.EstateManagers);
            SaveUUIDList(es.EstateID, "estate_users", es.EstateAccess);
            SaveUUIDList(es.EstateID, "estate_groups", es.EstateGroups);
        }

        private void LoadBanList(EstateSettings es)
        {
            es.ClearBans();

            SqliteCommand cmd = (SqliteCommand)m_connection.CreateCommand();

            cmd.CommandText = "select bannedUUID from estateban where EstateID = :EstateID";
            cmd.Parameters.Add(":EstateID", es.EstateID);

            IDataReader r = cmd.ExecuteReader();

            while (r.Read())
            {
                EstateBan eb = new EstateBan();

                UUID uuid = new UUID();
                UUID.TryParse(r["bannedUUID"].ToString(), out uuid);

                eb.BannedUserID = uuid;
                eb.BannedHostAddress = "0.0.0.0";
                eb.BannedHostIPMask = "0.0.0.0";
                es.AddBan(eb);
            }
            r.Close();
        }

        private void SaveBanList(EstateSettings es)
        {
            SqliteCommand cmd = (SqliteCommand)m_connection.CreateCommand();

            cmd.CommandText = "delete from estateban where EstateID = :EstateID";
            cmd.Parameters.Add(":EstateID", es.EstateID.ToString());

            cmd.ExecuteNonQuery();

            cmd.Parameters.Clear();

            cmd.CommandText = "insert into estateban (EstateID, bannedUUID, bannedIp, bannedIpHostMask, bannedNameMask) values ( :EstateID, :bannedUUID, '', '', '' )";

            foreach (EstateBan b in es.EstateBans)
            {
                cmd.Parameters.Add(":EstateID", es.EstateID.ToString());
                cmd.Parameters.Add(":bannedUUID", b.BannedUserID.ToString());

                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
            }
        }

        void SaveUUIDList(uint EstateID, string table, UUID[] data)
        {
            SqliteCommand cmd = (SqliteCommand)m_connection.CreateCommand();

            cmd.CommandText = "delete from "+table+" where EstateID = :EstateID";
            cmd.Parameters.Add(":EstateID", EstateID.ToString());

            cmd.ExecuteNonQuery();

            cmd.Parameters.Clear();

            cmd.CommandText = "insert into "+table+" (EstateID, uuid) values ( :EstateID, :uuid )";

            foreach (UUID uuid in data)
            {
                cmd.Parameters.Add(":EstateID", EstateID.ToString());
                cmd.Parameters.Add(":uuid", uuid.ToString());

                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
            }
        }

        UUID[] LoadUUIDList(uint EstateID, string table)
        {
            List<UUID> uuids = new List<UUID>();

            SqliteCommand cmd = (SqliteCommand)m_connection.CreateCommand();

            cmd.CommandText = "select uuid from "+table+" where EstateID = :EstateID";
            cmd.Parameters.Add(":EstateID", EstateID);

            IDataReader r = cmd.ExecuteReader();

            while (r.Read())
            {
                // EstateBan eb = new EstateBan();

                UUID uuid = new UUID();
                UUID.TryParse(r["uuid"].ToString(), out uuid);

                uuids.Add(uuid);
            }
            r.Close();

            return uuids.ToArray();
        }
    }
}
