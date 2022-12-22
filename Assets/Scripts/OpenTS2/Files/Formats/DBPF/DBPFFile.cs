/*
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at
 * http://mozilla.org/MPL/2.0/. 
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using OpenTS2.Files.Utils;
using OpenTS2.Common;
using OpenTS2.Common.Utils;
using OpenTS2.Content;
using OpenTS2.Content.Changes;
using OpenTS2.Content.DBPF;

namespace OpenTS2.Files.Formats.DBPF
{
    /// <summary>
    /// The database-packed file (DBPF) is a format used to store data for pretty much all Maxis games after The Sims, 
    /// including The Sims Online (the first appearance of this format), SimCity 4, The Sims 2, Spore, The Sims 3, and 
    /// SimCity 2013.
    /// </summary>
    public class DBPFFile : IDisposable
    {
        public class DBPFFileChanges
        {
            private readonly DBPFFile _owner;
            private ContentProvider Provider
            {
                get
                {
                    return _owner.Provider;
                }
            }

            public bool Dirty = false;
            public Dictionary<ResourceKey, bool> DeletedEntries = new Dictionary<ResourceKey, bool>();
            public Dictionary<ResourceKey, AbstractChanged> ChangedEntries = new Dictionary<ResourceKey, AbstractChanged>();

            public DBPFFileChanges(DBPFFile owner)
            {
                this._owner = owner;
            }

            /// <summary>
            /// Mark all entries in this package as deleted.
            /// todo - be less lazy and make this more efficient.
            /// </summary>
            public void Delete()
            {
                Provider?.RemoveFromResourceMap(_owner);
                var entries = _owner.Entries;
                foreach(var element in entries)
                {
                    DeletedEntries[element.InternalTGI] = true;
                }
                Dirty = true;
                RefreshCache();
            }

            /// <summary>
            /// Revert all changes
            /// </summary>
            public void Clear()
            {
                Provider?.RemoveFromResourceMap(_owner);
                DeletedEntries.Clear();
                ChangedEntries.Clear();
                Dirty = false;
                Provider?.UpdateOrAddToResourceMap(_owner);
                RefreshCache();
            }

            void RefreshCache()
            {
                Provider?.Cache.RemoveAllForPackage(_owner);
            }

            void RefreshCache(ResourceKey tgi)
            {
                Provider?.Cache.Remove(tgi, _owner);
            }

            /// <summary>
            /// Mark an entry as deleted.
            /// </summary>
            /// <param name="entry">Entry to delete</param>
            public void Delete(DBPFEntry entry)
            {
                DeletedEntries[entry.InternalTGI] = true;
                Dirty = true;
                Provider?.RemoveFromResourceMap(entry);
                RefreshCache(entry.InternalTGI);
            }

            /// <summary>
            /// Unmark an entry as deleted.
            /// </summary>
            /// <param name="entry">Entry to undelete</param>
            public void Restore(DBPFEntry entry)
            {
                if (DeletedEntries.ContainsKey(entry.InternalTGI))
                {
                    DeletedEntries.Remove(entry.InternalTGI);
                    Dirty = true;
                    Provider?.UpdateOrAddToResourceMap(entry);
                    RefreshCache(entry.InternalTGI);
                }
            }

            /// <summary>
            /// Mark an entry as deleted by its TGI
            /// </summary>
            /// <param name="tgi">TGI of entry to delete.</param>
            public void Delete(ResourceKey tgi)
            {
                DeletedEntries[tgi] = true;
                Dirty = true;
                Provider?.RemoveFromResourceMap(tgi.LocalGroupID(_owner.GroupID), _owner);
                RefreshCache(tgi);
            }
            /// <summary>
            /// Unmark an entry as deleted by its TGI
            /// </summary>
            /// <param name="tgi">TGI of entry to undelete.</param>
            public void Restore(ResourceKey tgi)
            {
                if (DeletedEntries.ContainsKey(tgi))
                {
                    DeletedEntries.Remove(tgi);
                    Dirty = true;
                    Provider?.UpdateOrAddToResourceMap(_owner.GetEntryByTGI(tgi));
                    RefreshCache(tgi);
                }
            }

            /// <summary>
            /// Unmark an entry as deleted without updating cache.
            /// </summary>
            /// <param name="tgi">TGI of entry to undelete.</param>
            void InternalRestore(ResourceKey tgi)
            {
                if (DeletedEntries.ContainsKey(tgi))
                {
                    DeletedEntries.Remove(tgi);
                }
            }

            /// <summary>
            /// Save changes to an asset in memory.
            /// </summary>
            /// <param name="asset">Asset.</param>
            public void Set(AbstractAsset asset)
            {
                asset.Package = _owner;
                asset.GlobalTGI = asset.InternalTGI.LocalGroupID(_owner.GroupID);
                var changedAsset = new ChangedAsset(asset);
                ChangedEntries[asset.InternalTGI] = changedAsset;
                InternalRestore(asset.InternalTGI);
                Dirty = true;
                Provider?.UpdateOrAddToResourceMap(changedAsset.Entry);
                RefreshCache(asset.InternalTGI);
            }
            /// <summary>
            /// Save changes to a resource in memory.
            /// </summary>
            /// <param name="bytes">Resource file bytes.</param>
            /// <param name="tgi">Resource TGI.</param>
            /// <param name="compressed">Compress?</param>
            public void Set(byte[] bytes, ResourceKey tgi, bool compressed)
            {
                var changedFile = new ChangedFile(bytes, tgi, _owner, Codecs.Get(tgi.TypeID))
                {
                    Compressed = compressed
                };
                ChangedEntries[tgi] = changedFile;
                InternalRestore(tgi);
                Dirty = true;
                Provider?.UpdateOrAddToResourceMap(changedFile.Entry);
                RefreshCache(tgi);
            }
        }

        /// <summary>
        /// DIR resource at the time of deserialization.
        /// </summary>
        private DIRAsset _compressionDIR = null;
        public bool Deleted
        {
            get
            {
                return _deleted;
            }
        }
        bool _deleted = false;
        public bool DeleteIfEmpty = true;
        private readonly DBPFFileChanges _changes;
        public ContentProvider Provider = null;
        
        /// <summary>
        /// Holds all runtime modifications in memory.
        /// </summary>
        public DBPFFileChanges Changes
        {
            get
            {
                return _changes;
            }
        }
        private string _filePath = "";
        public string FilePath
        {
            get { return _filePath; }
            set
            {
                var oldProvider = Provider;
                oldProvider?.RemovePackage(this);
                _filePath = value;
                GroupID = FileUtils.GroupHash(Path.GetFileNameWithoutExtension(_filePath));
                foreach(var element in _entriesList)
                {
                    element.GlobalTGI = element.InternalTGI.LocalGroupID(GroupID);
                }
                foreach(var element in Changes.ChangedEntries)
                {
                    element.Value.Entry.GlobalTGI = element.Value.Entry.InternalTGI.LocalGroupID(GroupID);
                }
                oldProvider?.AddPackage(this);
            }
        }
        public int DateCreated;
        public int DateModified;

        public uint IndexMajorVersion;
        public uint IndexMinorVersion;
        private uint _numEntries;
        public uint GroupID;
        private IoBuffer _reader;

        /// <summary>
        /// Returns true if this package is empty.
        /// </summary>
        public bool Empty
        {
            get
            {
                return Entries.Count == 0;
            }
        }

        /// <summary>
        /// Get all entries in this package, plus modifications, minus deleted entries.
        /// </summary>
        public List<DBPFEntry> Entries
        {
            get
            {
                var basicEntries = OriginalEntries;
                var finalEntries = new List<DBPFEntry>();
                foreach(var element in basicEntries)
                {
                    if (Changes.DeletedEntries.ContainsKey(element.InternalTGI))
                        continue;
                    if (!Changes.ChangedEntries.ContainsKey(element.InternalTGI))
                        finalEntries.Add(element);
                }
                foreach(var element in Changes.ChangedEntries)
                {
                    finalEntries.Add(element.Value.Entry);
                }
                return finalEntries;
            }
        }

        /// <summary>
        /// Get all original entries in this package.
        /// </summary>
        public List<DBPFEntry> OriginalEntries
        {
            get
            {
                return _entriesList;
            }
        }
        private List<DBPFEntry> _entriesList = new List<DBPFEntry>();
        private Dictionary<ResourceKey, DBPFEntry> _entryByTGI = new Dictionary<ResourceKey, DBPFEntry>();
        //private Dictionary<ResourceKey, DBPFEntry> m_EntryByInternalTGI = new Dictionary<ResourceKey, DBPFEntry>();

        private Stream _stream;
        private IoBuffer _io;

        /// <summary>
        /// Constructs a new DBPF instance.
        /// </summary>
        public DBPFFile()
        {
            _changes = new DBPFFileChanges(this)
            {
                Dirty = true
            };
        }

        /// <summary>
        /// Creates a DBPF instance from a path.
        /// </summary>
        /// <param name="file">The path to an DBPF archive.</param>
        public DBPFFile(string file) : this()
        {
            _filePath = file;
            GroupID = FileUtils.GroupHash(Path.GetFileNameWithoutExtension(file));
            var stream = File.OpenRead(file);
            Read(stream);
            _changes.Dirty = false;
        }

        /// <summary>
        /// Reads a DBPF archive from a stream.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        public void Read(Stream stream)
        {
            _entryByTGI = new Dictionary<ResourceKey, DBPFEntry>();
            _entriesList = new List<DBPFEntry>();

            var io = IoBuffer.FromStream(stream, ByteOrder.LITTLE_ENDIAN);
            _reader = io;
            this._io = io;
            this._stream = stream;

            var magic = io.ReadCString(4);
            if (magic != "DBPF")
            {
                throw new Exception("Not a DBPF file");
            }

            var majorVersion = io.ReadUInt32();
            var minorVersion = io.ReadUInt32();
            var version = majorVersion + (((double)minorVersion) / 10.0);

            /** Unknown, set to 0 **/
            io.Skip(12);

            // Changed from FreeSO's "version == 1.0"
            if (version <= 1.2)
            {
                this.DateCreated = io.ReadInt32();
                this.DateModified = io.ReadInt32();
            }

            if (version < 2.0)
            {
                IndexMajorVersion = io.ReadUInt32();
            }

            _numEntries = io.ReadUInt32();
            uint indexOffset = 0;
            if (version < 2.0)
            {
                indexOffset = io.ReadUInt32();
            }
            var indexSize = io.ReadUInt32();
            if (version < 2.0)
            {
                var trashEntryCount = io.ReadUInt32();
                var trashIndexOffset = io.ReadUInt32();
                var trashIndexSize = io.ReadUInt32();
                IndexMinorVersion = io.ReadUInt32();
            }
            else if (version == 2.0)
            {
                IndexMinorVersion = io.ReadUInt32();
                indexOffset = io.ReadUInt32();
                io.Skip(4);
            }

            /** Padding **/
            io.Skip(32);

            io.Seek(SeekOrigin.Begin, indexOffset);
            for (int i = 0; i < _numEntries; i++)
            {
                var entry = new DBPFEntry();
                uint instanceHigh = 0x00000000;
                var TypeID = io.ReadUInt32();
                var EntryGroupID = io.ReadUInt32();
                var InternalGroupID = EntryGroupID;
                if (EntryGroupID == GroupIDs.Local)
                    EntryGroupID = GroupID;
                var InstanceID = io.ReadUInt32();
                if (IndexMinorVersion >= 2)
                    instanceHigh = io.ReadUInt32();
                entry.GlobalTGI = new ResourceKey(InstanceID, instanceHigh, EntryGroupID, TypeID);
                entry.InternalTGI = new ResourceKey(InstanceID, instanceHigh, InternalGroupID, TypeID);
                entry.FileOffset = io.ReadUInt32();
                entry.FileSize = io.ReadUInt32();
                entry.Package = this;

                _entriesList.Add(entry);
                _entryByTGI[entry.InternalTGI] = entry;
            }
            _compressionDIR = (DIRAsset)GetAssetByTGI(ResourceKey.DIR);
        }

        /// <summary>
        /// Write and clear all changes to FilePath.
        /// </summary>
        public void WriteToFile()
        {
            if (DeleteIfEmpty && Empty)
            {
                Dispose();
                Provider?.RemovePackage(this);
                File.Delete(FilePath);
                Changes.Clear();
                _deleted = true;
                return;
            }
            var data = Serialize();
            Dispose();
            Filesystem.Write(FilePath, data);
            var stream = File.OpenRead(FilePath);
            Read(stream);
            Changes.Clear();
            return;
        }
        /// <summary>
        /// Serializes package with all resource changes, additions and deletions.
        /// </summary>
        /// <returns>Package bytes</returns>
        public byte[] Serialize()
        {
            UpdateDIR();
            var wStream = new MemoryStream(0);
            var writer = new BinaryWriter(wStream);
            var dirAsset = GetAssetByTGI<DIRAsset>(ResourceKey.DIR);
            var entries = Entries;
            //HeeeADER
            writer.Write(new char[] { 'D', 'B', 'P', 'F' });
            //major version
            writer.Write((int)1);
            //minor version
            writer.Write((int)2);

            //unknown
            writer.Write(new byte[12]);

            //Date stuff
            writer.Write((int)0);
            writer.Write((int)0);

            //Index major
            writer.Write((int)7);

            //Num entries
            writer.Write((int)entries.Count);

            //Index offset
            var indexOff = wStream.Position;
            //Placeholder
            writer.Write((int)0);

            //Index size
            var indexSize = wStream.Position;
            //Placeholder
            writer.Write((int)0);

            //Trash Entry Stuff
            writer.Write((int)0);
            writer.Write((int)0);
            writer.Write((int)0);

            //Index Minor Ver
            writer.Write((int)2);
            //Padding
            writer.Write(new byte[32]);

            //Go back and write index offset
            var lastPos = wStream.Position;
            wStream.Position = indexOff;
            writer.Write((int)lastPos);
            wStream.Position = lastPos;

            var entryOffset = new List<long>();

            for(var i=0;i<entries.Count;i++)
            {
                var element = entries[i];
                writer.Write(element.InternalTGI.TypeID);
                writer.Write(element.InternalTGI.GroupID);
                writer.Write(element.InternalTGI.InstanceID);
                writer.Write(element.InternalTGI.InstanceHigh);
                entryOffset.Add(wStream.Position);
                writer.Write(0);
                //File Size
                writer.Write(element.FileSize);
            }

            //Write files
            for (var i = 0; i < entries.Count; i++)
            {
                var filePosition = wStream.Position;
                wStream.Position = entryOffset[i];
                writer.Write((int)filePosition);
                wStream.Position = filePosition;
                var entry = entries[i];
                var entryData = GetBytes(entry);
                if (dirAsset != null && dirAsset.GetUncompressedSize(entry.InternalTGI) != 0)
                {
                    entryData = DBPFCompression.Compress(entryData);
                    var lastPosition = wStream.Position;
                    wStream.Position = filePosition + 4;
                    writer.Write(entryData.Length);
                    wStream.Position = lastPosition;
                }
                writer.Write(entryData, 0, entryData.Length);
            }
            lastPos = wStream.Position;
            var siz = lastPos - indexOff;
            wStream.Position = indexSize;
            writer.Write((int)siz);
            wStream.Position = lastPos;
            var buffer = StreamUtils.GetBuffer(wStream);
            writer.Dispose();
            wStream.Dispose();
            return buffer;
        }

        void UpdateDIR()
        {
            var dirAsset = new DIRAsset();
            var entries = Entries;
            foreach(var element in entries)
            {
                if (element is DynamicDBPFEntry dynamicEntry)
                {
                    if (dynamicEntry.Change.Compressed)
                        dirAsset.SizeByInternalTGI[element.InternalTGI] = (uint)dynamicEntry.Change.Bytes.Length;
                }
                else
                {
                    var uncompressedSize = InternalGetUncompressedSize(element);
                    if (uncompressedSize > 0)
                        dirAsset.SizeByInternalTGI[element.InternalTGI] = uncompressedSize;
                }
            }
            if (dirAsset.SizeByInternalTGI.Count == 0)
            {
                Changes.Delete(ResourceKey.DIR);
                return;
            }
            dirAsset.Package = this;
            dirAsset.TGI = ResourceKey.DIR;
            dirAsset.Compressed = false;
            dirAsset.Save();
        }

        /// <summary>
        /// Gets a DBPFEntry's data from this DBPF instance.
        /// </summary>
        /// <param name="entry">Entry to retrieve data for.</param>
        /// <returns>Data for entry.</returns>
        public byte[] GetBytes(DBPFEntry entry, bool ignoreDeleted = true)
        {
            if (ignoreDeleted)
            {
                if (Changes.DeletedEntries.ContainsKey(entry.InternalTGI))
                    return null;
            }
            if (Changes.ChangedEntries.ContainsKey(entry.InternalTGI))
                return Changes.ChangedEntries[entry.InternalTGI].Bytes;
            _reader.Seek(SeekOrigin.Begin, entry.FileOffset);
            var fileBytes = _reader.ReadBytes((int)entry.FileSize);
            var uncompressedSize = InternalGetUncompressedSize(entry);
            if (uncompressedSize > 0)
            {
                return DBPFCompression.Decompress(fileBytes, uncompressedSize);
            }
            return fileBytes;
        }

        /// <summary>
        /// Gets an item from its TGI (Type, Group, Instance IDs)
        /// </summary>
        /// <param name="tgi">The TGI of the entry.</param>
        /// <returns>The entry's data.</returns>
        public byte[] GetBytesByTGI(ResourceKey tgi, bool ignoreDeleted = true)
        {
            if (ignoreDeleted)
            {
                if (Changes.DeletedEntries.ContainsKey(tgi))
                    return null;
            }
            if (Changes.ChangedEntries.ContainsKey(tgi))
                return Changes.ChangedEntries[tgi].Bytes;
            if (_entryByTGI.ContainsKey(tgi))
                return GetBytes(_entryByTGI[tgi]);
            else
                return null;
        }

        uint InternalGetUncompressedSize(DBPFEntry entry)
        {
            if (entry.InternalTGI.TypeID == TypeIDs.DIR)
                return 0;
            var dirAsset = _compressionDIR;
            if (dirAsset == null)
                return 0;
            if (dirAsset.SizeByInternalTGI.ContainsKey(entry.InternalTGI))
                return dirAsset.SizeByInternalTGI[entry.InternalTGI];
            return 0;
        }

        /// <summary>
        /// Gets an asset from its DBPF Entry
        /// </summary>
        /// <param name="entry">The DBPF Entry</param>
        /// <returns></returns>
        public AbstractAsset GetAsset<T>(DBPFEntry entry, bool ignoreDeleted = true) where T : AbstractAsset
        {
            return GetAsset(entry, ignoreDeleted) as T;
        }

        /// <summary>
        /// Gets an asset from its DBPF Entry
        /// </summary>
        /// <param name="entry">The DBPF Entry</param>
        /// <returns></returns>
        public AbstractAsset GetAsset(DBPFEntry entry, bool ignoreDeleted = true)
        {
            if (Changes.DeletedEntries.ContainsKey(entry.InternalTGI) && ignoreDeleted)
                return null;
            if (Changes.ChangedEntries.ContainsKey(entry.InternalTGI))
                return Changes.ChangedEntries[entry.InternalTGI].Asset;
            var item = GetBytes(entry, ignoreDeleted);
            var codec = Codecs.Get(entry.GlobalTGI.TypeID);
            var asset = codec.Deserialize(item, entry.GlobalTGI, this);
            asset.Compressed = InternalGetUncompressedSize(entry) > 0;
            asset.GlobalTGI = entry.GlobalTGI;
            asset.InternalTGI = entry.InternalTGI;
            asset.Package = this;
            return asset;
        }

        public T GetAssetByTGI<T>(ResourceKey tgi, bool ignoreDeleted = true) where T : AbstractAsset
        {
            return GetAssetByTGI(tgi, ignoreDeleted) as T;
        }
        public AbstractAsset GetAssetByTGI(ResourceKey tgi, bool ignoreDeleted = true)
        {
            var entry = GetEntryByTGI(tgi, ignoreDeleted);
            if (entry != null)
                return GetAsset(entry, ignoreDeleted);
            else
                return null;
        }

        /// <summary>
        /// Gets an entry from its TGI (Type, Group, Instance IDs)
        /// </summary>
        /// <param name="tgi">The TGI of the entry.</param>
        /// <returns>The entry.</returns>
        public DBPFEntry GetEntryByTGI(ResourceKey tgi , bool ignoreDeleted = true)
        {
            if (Changes.DeletedEntries.ContainsKey(tgi) && ignoreDeleted)
                return null;
            if (Changes.ChangedEntries.ContainsKey(tgi))
                return Changes.ChangedEntries[tgi].Entry;
            if (_entryByTGI.ContainsKey(tgi))
                return _entryByTGI[tgi];
            else
                return null;
        }

        #region IDisposable Members

        /// <summary>
        /// Disposes this DBPF instance.
        /// </summary>
        public void Dispose()
        {
            _stream?.Dispose();
            _io?.Dispose();
        }

        #endregion
    }
}