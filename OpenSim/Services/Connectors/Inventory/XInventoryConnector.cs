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
 *     * Neither the name of the OpenSimulator Project nor the
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

using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Communications;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using OpenSim.Server.Base;
using OpenMetaverse;

namespace OpenSim.Services.Connectors
{
    public class XInventoryServicesConnector : IInventoryService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private string m_ServerURI = String.Empty;

        public XInventoryServicesConnector()
        {
        }

        public XInventoryServicesConnector(string serverURI)
        {
            m_ServerURI = serverURI.TrimEnd('/');
        }

        public XInventoryServicesConnector(IConfigSource source)
        {
            Initialise(source);
        }

        public virtual void Initialise(IConfigSource source)
        {
            IConfig assetConfig = source.Configs["InventoryService"];
            if (assetConfig == null)
            {
                m_log.Error("[INVENTORY CONNECTOR]: InventoryService missing from OpanSim.ini");
                throw new Exception("Inventory connector init error");
            }

            string serviceURI = assetConfig.GetString("InventoryServerURI",
                    String.Empty);

            if (serviceURI == String.Empty)
            {
                m_log.Error("[INVENTORY CONNECTOR]: No Server URI named in section InventoryService");
                throw new Exception("Inventory connector init error");
            }
            m_ServerURI = serviceURI;
        }

        public bool CreateUserInventory(UUID principalID)
        {
            Dictionary<string,object> ret = MakeRequest("CREATEUSERINVENTORY",
                    new Dictionary<string,object> {
                        { "PRINCIPAL", principalID.ToString() }
                    });

            if (ret == null)
                return false;

            return bool.Parse(ret["RESULT"].ToString());
        }

        public List<InventoryFolderBase> GetInventorySkeleton(UUID principalID)
        {
            Dictionary<string,object> ret = MakeRequest("GETINVENTORYSKELETON",
                    new Dictionary<string,object> {
                        { "PRINCIPAL", principalID.ToString() }
                    });

            if (ret == null)
                return null;

            List<InventoryFolderBase> folders = new List<InventoryFolderBase>();

            foreach (Object o in ret.Values)
                folders.Add(BuildFolder((Dictionary<string,object>)o));

            return folders;
        }

        public InventoryFolderBase GetRootFolder(UUID principalID)
        {
            Dictionary<string,object> ret = MakeRequest("GETROOTFOLDER",
                    new Dictionary<string,object> {
                        { "PRINCIPAL", principalID.ToString() }
                    });

            if (ret == null)
                return null;

            if (ret.Count == 0)
                return null;

            return BuildFolder(ret);
        }

        public InventoryFolderBase GetFolderForType(UUID principalID, AssetType type)
        {
            Dictionary<string,object> ret = MakeRequest("GETFOLDERFORTYPE",
                    new Dictionary<string,object> {
                        { "PRINCIPAL", principalID.ToString() },
                        { "TYPE", ((int)type).ToString() }
                    });

            if (ret == null)
                return null;

            if (ret.Count == 0)
                return null;

            return BuildFolder(ret);
        }

        public InventoryCollection GetFolderContent(UUID principalID, UUID folderID)
        {
            Dictionary<string,object> ret = MakeRequest("GETFOLDERCONTENT",
                    new Dictionary<string,object> {
                        { "PRINCIPAL", principalID.ToString() },
                        { "FOLDER", folderID.ToString() }
                    });

            if (ret == null)
                return null;

            if (ret.Count == 0)
                return null;

            
            InventoryCollection inventory = new InventoryCollection();
            inventory.Folders = new List<InventoryFolderBase>();
            inventory.Items = new List<InventoryItemBase>();
            inventory.UserID = principalID;
            
            Dictionary<string,object> folders =
                    (Dictionary<string,object>)ret["FOLDERS"];
            Dictionary<string,object> items =
                    (Dictionary<string,object>)ret["ITEMS"];

            foreach (Object o in folders.Values)
                inventory.Folders.Add(BuildFolder((Dictionary<string,object>)o));
            foreach (Object o in items.Values)
                inventory.Items.Add(BuildItem((Dictionary<string,object>)o));

            return inventory;
        }

        public List<InventoryItemBase> GetFolderItems(UUID principalID, UUID folderID)
        {
            Dictionary<string,object> ret = MakeRequest("GETFOLDERCONTENT",
                    new Dictionary<string,object> {
                        { "PRINCIPAL", principalID.ToString() },
                        { "FOLDER", folderID.ToString() }
                    });

            if (ret == null)
                return null;

            if (ret.Count == 0)
                return null;

            
            List<InventoryItemBase> items = new List<InventoryItemBase>();
            
            foreach (Object o in ret.Values)
                items.Add(BuildItem((Dictionary<string,object>)o));

            return items;
        }

        public bool AddFolder(InventoryFolderBase folder)
        {
            Dictionary<string,object> ret = MakeRequest("ADDFOLDER",
                    new Dictionary<string,object> {
                        { "ParentID", folder.ParentID.ToString() },
                        { "Type", folder.Type.ToString() },
                        { "Version", folder.Version.ToString() },
                        { "Name", folder.Name.ToString() },
                        { "Owner", folder.Owner.ToString() },
                        { "ID", folder.ID.ToString() }
                    });

            if (ret == null)
                return false;

            return bool.Parse(ret["RESULT"].ToString());
        }

        public bool UpdateFolder(InventoryFolderBase folder)
        {
            Dictionary<string,object> ret = MakeRequest("UPDATEFOLDER",
                    new Dictionary<string,object> {
                        { "ParentID", folder.ParentID.ToString() },
                        { "Type", folder.Type.ToString() },
                        { "Version", folder.Version.ToString() },
                        { "Name", folder.Name.ToString() },
                        { "Owner", folder.Owner.ToString() },
                        { "ID", folder.ID.ToString() }
                    });

            if (ret == null)
                return false;

            return bool.Parse(ret["RESULT"].ToString());
        }

        public bool MoveFolder(InventoryFolderBase folder)
        {
            Dictionary<string,object> ret = MakeRequest("MOVEFOLDER",
                    new Dictionary<string,object> {
                        { "ParentID", folder.ParentID.ToString() },
                        { "ID", folder.ID.ToString() }
                    });

            if (ret == null)
                return false;

            return bool.Parse(ret["RESULT"].ToString());
        }

        public bool DeleteFolders(UUID principalID, List<UUID> folderIDs)
        {
            List<string> slist = new List<string>();

            foreach (UUID f in folderIDs)
                slist.Add(f.ToString());

            Dictionary<string,object> ret = MakeRequest("DELETEFOLDERS",
                    new Dictionary<string,object> {
                        { "PRINCIPAL", principalID.ToString() },
                        { "FOLDERS", slist }
                    });

            if (ret == null)
                return false;

            return bool.Parse(ret["RESULT"].ToString());
        }

        public bool PurgeFolder(InventoryFolderBase folder)
        {
            Dictionary<string,object> ret = MakeRequest("PURGEFOLDER",
                    new Dictionary<string,object> {
                        { "ID", folder.ID.ToString() }
                    });

            if (ret == null)
                return false;

            return bool.Parse(ret["RESULT"].ToString());
        }

        public bool AddItem(InventoryItemBase item)
        {
            Dictionary<string,object> ret = MakeRequest("ADDITEM",
                    new Dictionary<string,object> {
                        { "AssetID", item.AssetID.ToString() },
                        { "AssetType", item.AssetType.ToString() },
                        { "Name", item.Name.ToString() },
                        { "Owner", item.Owner.ToString() },
                        { "ID", item.ID.ToString() },
                        { "InvType", item.InvType.ToString() },
                        { "Folder", item.Folder.ToString() },
                        { "CreatorId", item.CreatorId.ToString() },
                        { "Description", item.Description.ToString() },
                        { "NextPermissions", item.NextPermissions.ToString() },
                        { "CurrentPermissions", item.CurrentPermissions.ToString() },
                        { "BasePermissions", item.BasePermissions.ToString() },
                        { "EveryOnePermissions", item.EveryOnePermissions.ToString() },
                        { "GroupPermissions", item.GroupPermissions.ToString() },
                        { "GroupID", item.GroupID.ToString() },
                        { "GroupOwned", item.GroupOwned.ToString() },
                        { "SalePrice", item.SalePrice.ToString() },
                        { "SaleType", item.SaleType.ToString() },
                        { "Flags", item.Flags.ToString() },
                        { "CreationDate", item.CreationDate.ToString() }
                    });

            if (ret == null)
                return false;

            return bool.Parse(ret["RESULT"].ToString());
        }

        public bool UpdateItem(InventoryItemBase item)
        {
            Dictionary<string,object> ret = MakeRequest("UPDATEITEM",
                    new Dictionary<string,object> {
                        { "AssetID", item.AssetID.ToString() },
                        { "AssetType", item.AssetType.ToString() },
                        { "Name", item.Name.ToString() },
                        { "Owner", item.Owner.ToString() },
                        { "ID", item.ID.ToString() },
                        { "InvType", item.InvType.ToString() },
                        { "Folder", item.Folder.ToString() },
                        { "CreatorId", item.CreatorId.ToString() },
                        { "Description", item.Description.ToString() },
                        { "NextPermissions", item.NextPermissions.ToString() },
                        { "CurrentPermissions", item.CurrentPermissions.ToString() },
                        { "BasePermissions", item.BasePermissions.ToString() },
                        { "EveryOnePermissions", item.EveryOnePermissions.ToString() },
                        { "GroupPermissions", item.GroupPermissions.ToString() },
                        { "GroupID", item.GroupID.ToString() },
                        { "GroupOwned", item.GroupOwned.ToString() },
                        { "SalePrice", item.SalePrice.ToString() },
                        { "SaleType", item.SaleType.ToString() },
                        { "Flags", item.Flags.ToString() },
                        { "CreationDate", item.CreationDate.ToString() }
                    });

            if (ret == null)
                return false;

            return bool.Parse(ret["RESULT"].ToString());
        }

        public bool MoveItems(UUID principalID, List<InventoryItemBase> items)
        {
            List<string> idlist = new List<string>();
            List<string> destlist = new List<string>();

            foreach (InventoryItemBase item in items)
            {
                idlist.Add(item.ID.ToString());
                destlist.Add(item.Folder.ToString());
            }

            Dictionary<string,object> ret = MakeRequest("MOVEITEMS",
                    new Dictionary<string,object> {
                        { "PrincipalID", principalID.ToString() },
                        { "IDLIST", idlist },
                        { "DESTLIST", destlist }
                    });

            if (ret == null)
                return false;

            return bool.Parse(ret["RESULT"].ToString());
        }

        public bool DeleteItems(UUID principalID, List<UUID> itemIDs)
        {
            List<string> slist = new List<string>();

            foreach (UUID f in itemIDs)
                slist.Add(f.ToString());

            Dictionary<string,object> ret = MakeRequest("DELETEITEMS",
                    new Dictionary<string,object> {
                        { "PRINCIPAL", principalID.ToString() },
                        { "ITEMS", slist }
                    });

            if (ret == null)
                return false;

            return bool.Parse(ret["RESULT"].ToString());
        }

        public InventoryItemBase GetItem(InventoryItemBase item)
        {
            Dictionary<string,object> ret = MakeRequest("GETITEM",
                    new Dictionary<string,object> {
                        { "ID", item.ID.ToString() }
                    });

            if (ret == null)
                return null;

            if (ret.Count == 0)
                return null;

            return BuildItem(ret);
        }

        public InventoryFolderBase GetFolder(InventoryFolderBase folder)
        {
            Dictionary<string,object> ret = MakeRequest("GETFOLDER",
                    new Dictionary<string,object> {
                        { "ID", folder.ID.ToString() }
                    });

            if (ret == null)
                return null;

            if (ret.Count == 0)
                return null;

            return BuildFolder(ret);
        }

        public List<InventoryItemBase> GetActiveGestures(UUID principalID)
        {
            Dictionary<string,object> ret = MakeRequest("GETACTIVEGESTURES",
                    new Dictionary<string,object> {
                        { "PRINCIPAL", principalID.ToString() }
                    });

            if (ret == null)
                return null;

            List<InventoryItemBase> items = new List<InventoryItemBase>();

            foreach (Object o in ret.Values)
                items.Add(BuildItem((Dictionary<string,object>)o));

            return items;
        }

        public int GetAssetPermissions(UUID principalID, UUID assetID)
        {
            Dictionary<string,object> ret = MakeRequest("GETASSETPERMISSIONS",
                    new Dictionary<string,object> {
                        { "PRINCIPAL", principalID.ToString() },
                        { "ASSET", assetID.ToString() }
                    });

            if (ret == null)
                return 0;

            return int.Parse(ret["RESULT"].ToString());
        }


        // These are either obsolete or unused
        //
        public InventoryCollection GetUserInventory(UUID principalID)
        {
            return null;
        }

        public void GetUserInventory(UUID principalID, InventoryReceiptCallback callback)
        {
        }

        public bool HasInventoryForUser(UUID principalID)
        {
            return false;
        }

        // Helpers
        //
        private Dictionary<string,object> MakeRequest(string method,
                Dictionary<string,object> sendData)
        {
            sendData["METHOD"] = method;

            string reply = SynchronousRestFormsRequester.MakeRequest("POST",
                    m_ServerURI + "/xinventory",
                    ServerUtils.BuildQueryString(sendData));

            Dictionary<string, object> replyData = ServerUtils.ParseXmlResponse(
                    reply);

            return replyData;
        }

        private InventoryFolderBase BuildFolder(Dictionary<string,object> data)
        {
            InventoryFolderBase folder = new InventoryFolderBase();

            folder.ParentID =  new UUID(data["ParentID"].ToString());
            folder.Type = short.Parse(data["Type"].ToString());
            folder.Version = ushort.Parse(data["Version"].ToString());
            folder.Name = data["Name"].ToString();
            folder.Owner =  new UUID(data["Owner"].ToString());
            folder.ID = new UUID(data["ID"].ToString());
            
            return folder;
        }

        private InventoryItemBase BuildItem(Dictionary<string,object> data)
        {
            InventoryItemBase item = new InventoryItemBase();

            item.AssetID = new UUID(data["AssetID"].ToString());
            item.AssetType = int.Parse(data["AssetType"].ToString());
            item.Name = data["Name"].ToString();
            item.Owner = new UUID(data["Owner"].ToString());
            item.ID = new UUID(data["ID"].ToString());
            item.InvType = int.Parse(data["InvType"].ToString());
            item.Folder = new UUID(data["Folder"].ToString());
            item.CreatorId = data["CreatorId"].ToString();
            item.Description = data["Description"].ToString();
            item.NextPermissions = uint.Parse(data["NextPermissions"].ToString());
            item.CurrentPermissions = uint.Parse(data["CurrentPermissions"].ToString());
            item.BasePermissions = uint.Parse(data["BasePermissions"].ToString());
            item.EveryOnePermissions = uint.Parse(data["EveryOnePermissions"].ToString());
            item.GroupPermissions = uint.Parse(data["GroupPermissions"].ToString());
            item.GroupID = new UUID(data["GroupID"].ToString());
            item.GroupOwned = bool.Parse(data["GroupOwned"].ToString());
            item.SalePrice = int.Parse(data["SalePrice"].ToString());
            item.SaleType = byte.Parse(data["SaleType"].ToString());
            item.Flags = uint.Parse(data["Flags"].ToString());
            item.CreationDate = int.Parse(data["CreationDate"].ToString());

            return item;
        }
    }
}
