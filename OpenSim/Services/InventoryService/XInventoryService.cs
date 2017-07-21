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

using System;
using System.Collections.Generic;
using OpenMetaverse;
using log4net;
using Nini.Config;
using System.Reflection;
using OpenSim.Services.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Data;
using OpenSim.Framework;

namespace OpenSim.Services.InventoryService
{
    public class XInventoryService : ServiceBase, IInventoryService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        protected IXInventoryData m_Database;

        public XInventoryService(IConfigSource config) : base(config)
        {
            string dllName = String.Empty;
            string connString = String.Empty;
            //string realm = "Inventory"; // OSG version doesn't use this

            //
            // Try reading the [InventoryService] section first, if it exists
            //
            IConfig authConfig = config.Configs["InventoryService"];
            if (authConfig != null)
            {
                dllName = authConfig.GetString("StorageProvider", dllName);
                connString = authConfig.GetString("ConnectionString", connString);
                // realm = authConfig.GetString("Realm", realm);
            }

            //
            // Try reading the [DatabaseService] section, if it exists
            //
            IConfig dbConfig = config.Configs["DatabaseService"];
            if (dbConfig != null)
            {
                if (dllName == String.Empty)
                    dllName = dbConfig.GetString("StorageProvider", String.Empty);
                if (connString == String.Empty)
                    connString = dbConfig.GetString("ConnectionString", String.Empty);
            }

            //
            // We tried, but this doesn't exist. We can't proceed.
            //
            if (dllName == String.Empty)
                throw new Exception("No StorageProvider configured");

            m_Database = LoadPlugin<IXInventoryData>(dllName,
                    new Object[] {connString, String.Empty});
            if (m_Database == null)
                throw new Exception("Could not find a storage interface in the given module");
        }

        public bool CreateUserInventory(UUID principalID)
        {
            // This is braindeaad. We can't ever communicate that we fixed
            // an existing inventory. Well, just return root folder status,
            // but check sanity anyway.
            //
            bool result = false;

            InventoryFolderBase rootFolder = GetRootFolder(principalID);

            if (rootFolder == null)
            {
                rootFolder = ConvertToOpenSim(CreateFolder(principalID, UUID.Zero, (int)AssetType.Folder, "My Inventory"));
                result = true;
            }

            XInventoryFolder[] sysFolders = GetSystemFolders(principalID);

            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.Animation) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.Animation, "Animations");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.Bodypart) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.Bodypart, "Body Parts");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.CallingCard) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.CallingCard, "Calling Cards");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.Clothing) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.Clothing, "Clothing");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.Gesture) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.Gesture, "Gestures");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.Landmark) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.Landmark, "Landmarks");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.LostAndFoundFolder) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.LostAndFoundFolder, "Lost And Found");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.Notecard) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.Notecard, "Notecards");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.Object) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.Object, "Objects");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.SnapshotFolder) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.SnapshotFolder, "Photo Album");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.LSLText) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.LSLText, "Scripts");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.Sound) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.Sound, "Sounds");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.Texture) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.Texture, "Textures");
            if (!Array.Exists(sysFolders, delegate (XInventoryFolder f) { if (f.type == (int)AssetType.TrashFolder) return true; return false; }))
                CreateFolder(principalID, rootFolder.ID, (int)AssetType.TrashFolder, "Trash");

            return result;
        }

        private XInventoryFolder CreateFolder(UUID principalID, UUID parentID, int type, string name)
        {
            XInventoryFolder newFolder = new XInventoryFolder();

            newFolder.folderName = name;
            newFolder.type = type;
            newFolder.version = 1;
            newFolder.folderID = UUID.Random();
            newFolder.agentID = principalID;
            newFolder.parentFolderID = parentID;

            m_Database.StoreFolder(newFolder);

            return newFolder;
        }

        private XInventoryFolder[] GetSystemFolders(UUID principalID)
        {
            XInventoryFolder[] allFolders = m_Database.GetFolders(
                    new string[] { "agentID" },
                    new string[] { principalID.ToString() });

            XInventoryFolder[] sysFolders = Array.FindAll(
                    allFolders,
                    delegate (XInventoryFolder f)
                    {
                        if (f.type > 0)
                            return true;
                        return false;
                    });

            return sysFolders;
        }

        public List<InventoryFolderBase> GetInventorySkeleton(UUID principalID)
        {
            XInventoryFolder[] allFolders = m_Database.GetFolders(
                    new string[] { "agentID" },
                    new string[] { principalID.ToString() });

            if (allFolders.Length == 0)
                return null;

            List<InventoryFolderBase> folders = new List<InventoryFolderBase>();

            foreach (XInventoryFolder x in allFolders)
            {
                m_log.DebugFormat("[INVENTORY]: Adding folder {0} to skeleton", x.folderName);
                folders.Add(ConvertToOpenSim(x));
            }

            return folders;
        }

        public InventoryFolderBase GetRootFolder(UUID principalID)
        {
            XInventoryFolder[] folders = m_Database.GetFolders(
                    new string[] { "agentID", "parentFolderID"},
                    new string[] { principalID.ToString(), UUID.Zero.ToString() });

            if (folders.Length == 0)
                return null;

            return ConvertToOpenSim(folders[0]);
        }

        public InventoryFolderBase GetFolderForType(UUID principalID, AssetType type)
        {
            XInventoryFolder[] folders = m_Database.GetFolders(
                    new string[] { "agentID", "type"},
                    new string[] { principalID.ToString(), ((int)type).ToString() });

            if (folders.Length == 0)
                return null;

            return ConvertToOpenSim(folders[0]);
        }

        public InventoryCollection GetFolderContent(UUID principalID, UUID folderID)
        {
            // This method doesn't receive a valud principal id from the
            // connector. So we disregard the principal and look
            // by ID.
            //
            m_log.DebugFormat("[INVENTORY]: Fetch contents for folder {0}", folderID.ToString());
            InventoryCollection inventory = new InventoryCollection();
            inventory.UserID = principalID;
            inventory.Folders = new List<InventoryFolderBase>();
            inventory.Items = new List<InventoryItemBase>();

            XInventoryFolder[] folders = m_Database.GetFolders(
                    new string[] { "parentFolderID"},
                    new string[] { folderID.ToString() });

            foreach (XInventoryFolder x in folders)
            {
                m_log.DebugFormat("[INVENTORY]: Adding folder {0} to response", x.folderName);
                inventory.Folders.Add(ConvertToOpenSim(x));
            }

            XInventoryItem[] items = m_Database.GetItems(
                    new string[] { "parentFolderID"},
                    new string[] { folderID.ToString() });

            foreach (XInventoryItem i in items)
            {
                m_log.DebugFormat("[INVENTORY]: Adding item {0} to response", i.inventoryName);
                inventory.Items.Add(ConvertToOpenSim(i));
            }

            return inventory;
        }
        
        public List<InventoryItemBase> GetFolderItems(UUID principalID, UUID folderID)
        {
            // Since we probably don't get a valid principal here, either ...
            //
            List<InventoryItemBase> invItems = new List<InventoryItemBase>();

            XInventoryItem[] items = m_Database.GetItems(
                    new string[] { "parentFolderID"},
                    new string[] { UUID.Zero.ToString() });

            foreach (XInventoryItem i in items)
                invItems.Add(ConvertToOpenSim(i));

            return invItems;
        }

        public bool AddFolder(InventoryFolderBase folder)
        {
            XInventoryFolder xFolder = ConvertFromOpenSim(folder);
            return m_Database.StoreFolder(xFolder);
        }

        public bool UpdateFolder(InventoryFolderBase folder)
        {
            return AddFolder(folder);
        }

        public bool MoveFolder(InventoryFolderBase folder)
        {
            XInventoryFolder[] x = m_Database.GetFolders(
                    new string[] { "folderID" },
                    new string[] { folder.ID.ToString() });

            if (x.Length == 0)
                return false;

            x[0].parentFolderID = folder.ParentID;

            return m_Database.StoreFolder(x[0]);
        }

        // We don't check the principal's ID here
        //
        public bool DeleteFolders(UUID principalID, List<UUID> folderIDs)
        {
            // Ignore principal ID, it's bogus at connector level
            //
            foreach (UUID id in folderIDs)
            {
                InventoryFolderBase f = new InventoryFolderBase();
                f.ID = id;
                PurgeFolder(f);
                m_Database.DeleteFolders("folderID", id.ToString());
            }

            return true;
        }

        public bool PurgeFolder(InventoryFolderBase folder)
        {
            XInventoryFolder[] subFolders = m_Database.GetFolders(
                    new string[] { "parentFolderID" },
                    new string[] { folder.ID.ToString() });

            foreach (XInventoryFolder x in subFolders)
            {
                PurgeFolder(ConvertToOpenSim(x));
                m_Database.DeleteFolders("folderID", x.folderID.ToString());
            }

            m_Database.DeleteItems("parentFolderID", folder.ID.ToString());

            return true;
        }

        public bool AddItem(InventoryItemBase item)
        {
            return m_Database.StoreItem(ConvertFromOpenSim(item));
        }

        public bool UpdateItem(InventoryItemBase item)
        {
            return m_Database.StoreItem(ConvertFromOpenSim(item));
        }

        public bool MoveItems(UUID principalID, List<InventoryItemBase> items)
        {
            // Principal is b0rked. *sigh*
            //
            foreach (InventoryItemBase i in items)
            {
                m_Database.MoveItem(i.ID.ToString(), i.Folder.ToString());
            }

            return true;
        }

        public bool DeleteItems(UUID principalID, List<UUID> itemIDs)
        {
            // Just use the ID... *facepalms*
            //
            foreach (UUID id in itemIDs)
                m_Database.DeleteItems("inventoryID", id.ToString());

            return true;
        }

        public InventoryItemBase GetItem(InventoryItemBase item)
        {
            XInventoryItem[] items = m_Database.GetItems(
                    new string[] { "inventoryID" },
                    new string[] { item.ID.ToString() });

            if (items.Length == 0)
                return null;

            return ConvertToOpenSim(items[0]);
        }

        public InventoryFolderBase GetFolder(InventoryFolderBase folder)
        {
            XInventoryFolder[] folders = m_Database.GetFolders(
                    new string[] { "folderID"},
                    new string[] { folder.ID.ToString() });

            if (folders.Length == 0)
                return null;

            return ConvertToOpenSim(folders[0]);
        }

        public List<InventoryItemBase> GetActiveGestures(UUID principalID)
        {
            XInventoryItem[] items = m_Database.GetActiveGestures(principalID);

            if (items.Length == 0)
                return null;

            List<InventoryItemBase> ret = new List<InventoryItemBase>();
            
            foreach (XInventoryItem x in items)
                ret.Add(ConvertToOpenSim(x));

            return ret;
        }

        public int GetAssetPermissions(UUID principalID, UUID assetID)
        {
            return m_Database.GetAssetPermissions(principalID, assetID);
        }

        // CM never needed those. Left unimplemented.
        // Obsolete in core
        //
        public InventoryCollection GetUserInventory(UUID userID)
        {
            return null;
        }
        public void GetUserInventory(UUID userID, InventoryReceiptCallback callback)
        {
        }

        // Unused.
        //
        public bool HasInventoryForUser(UUID userID)
        {
            return false;
        }

        // CM Helpers
        //
        private InventoryFolderBase ConvertToOpenSim(XInventoryFolder folder)
        {
            InventoryFolderBase newFolder = new InventoryFolderBase();

            newFolder.ParentID = folder.parentFolderID;
            newFolder.Type = (short)folder.type;
            newFolder.Version = (ushort)folder.version;
            newFolder.Name = folder.folderName;
            newFolder.Owner = folder.agentID;
            newFolder.ID = folder.folderID;

            return newFolder;
        }

        private XInventoryFolder ConvertFromOpenSim(InventoryFolderBase folder)
        {
            XInventoryFolder newFolder = new XInventoryFolder();

            newFolder.parentFolderID = folder.ParentID;
            newFolder.type = (int)folder.Type;
            newFolder.version = (int)folder.Version;
            newFolder.folderName = folder.Name;
            newFolder.agentID = folder.Owner;
            newFolder.folderID = folder.ID;

            return newFolder;
        }

        private InventoryItemBase ConvertToOpenSim(XInventoryItem item)
        {
            InventoryItemBase newItem = new InventoryItemBase();

            newItem.AssetID = item.assetID;
            newItem.AssetType = item.assetType;
            newItem.Name = item.inventoryName;
            newItem.Owner = item.avatarID;
            newItem.ID = item.inventoryID;
            newItem.InvType = item.invType;
            newItem.Folder = item.parentFolderID;
            newItem.CreatorId = item.creatorID.ToString();
            newItem.Description = item.inventoryDescription;
            newItem.NextPermissions = (uint)item.inventoryNextPermissions;
            newItem.CurrentPermissions = (uint)item.inventoryCurrentPermissions;
            newItem.BasePermissions = (uint)item.inventoryBasePermissions;
            newItem.EveryOnePermissions = (uint)item.inventoryEveryOnePermissions;
            newItem.GroupPermissions = (uint)item.inventoryGroupPermissions;
            newItem.GroupID = item.groupID;
            newItem.GroupOwned = item.groupOwned;
            newItem.SalePrice = item.salePrice;
            newItem.SaleType = (byte)item.saleType;
            newItem.Flags = (uint)item.flags;
            newItem.CreationDate = item.creationDate;

            return newItem;
        }

        private XInventoryItem ConvertFromOpenSim(InventoryItemBase item)
        {
            XInventoryItem newItem = new XInventoryItem();

            newItem.assetID = item.AssetID;
            newItem.assetType = item.AssetType;
            newItem.inventoryName = item.Name;
            newItem.avatarID = item.Owner;
            newItem.inventoryID = item.ID;
            newItem.invType = item.InvType;
            newItem.parentFolderID = item.Folder;
            newItem.creatorID = item.CreatorIdAsUuid;
            newItem.inventoryDescription = item.Description;
            newItem.inventoryNextPermissions = (int)item.NextPermissions;
            newItem.inventoryCurrentPermissions = (int)item.CurrentPermissions;
            newItem.inventoryBasePermissions = (int)item.BasePermissions;
            newItem.inventoryEveryOnePermissions = (int)item.EveryOnePermissions;
            newItem.inventoryGroupPermissions = (int)item.GroupPermissions;
            newItem.groupID = item.GroupID;
            newItem.groupOwned = item.GroupOwned;
            newItem.salePrice = item.SalePrice;
            newItem.saleType = (int)item.SaleType;
            newItem.flags = (int)item.Flags;
            newItem.creationDate = item.CreationDate;

            return newItem;
        }
    }
}
