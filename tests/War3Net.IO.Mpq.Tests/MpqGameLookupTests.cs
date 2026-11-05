namespace War3Net.IO.Mpq.Tests
{
    /// <summary>
    /// Archives as the game reads them: it finds a file from the hash table slot its name hashes to, forward to the first
    /// empty slot, stepping over deleted entries; and a map's archive may have bytes before its header (World Editor's
    /// HM3W block).
    /// </summary>
    [TestClass]
    public class MpqGameLookupTests
    {
        private const ushort TableSize = 16;
        private const uint Mask = TableSize - 1;

        [TestMethod]
        public void TestHeaderFieldsAfterPreArchiveData()
        {
            using var stream = new MemoryStream();
            stream.Write(new byte[512]);
            using (MpqArchive.Create(stream, new[] { File("a.txt") }, Options(), leaveOpen: true))
            {
            }

            var bytes = stream.ToArray();
            Assert.AreEqual(MpqHeader.MpqId, BitConverter.ToUInt32(bytes, 512));
            Assert.AreEqual(32u, BitConverter.ToUInt32(bytes, 512 + 4), "header size");
            Assert.AreEqual((uint)(bytes.Length - 512), BitConverter.ToUInt32(bytes, 512 + 8), "archive size");

            stream.Position = 0;
            using var archive = MpqArchive.Open(stream);
            Assert.AreEqual(512u, archive.Header.HeaderOffset);
            Assert.AreEqual((ushort)3, archive.Header.BlockSize);
            Assert.IsTrue(archive.FileExists("a.txt"));
        }

        [TestMethod]
        public void TestFileBeyondAnEmptySlotIsNotFound()
        {
            const string name = "orphan.txt";
            var home = MpqHash.GetIndex(name, Mask);
            using var archive = Reopened(Orphan(name, (home + 1) & Mask, 0));

            Assert.IsTrue(archive.GetHashTable()[(int)home].IsEmpty);
            Assert.IsFalse(archive.FileExists(name));
            Assert.AreEqual(-1, archive.FindHashIndex(name, MpqLocale.Neutral));
        }

        [TestMethod]
        public void TestFileAfterItsCollisionsIsFound()
        {
            const string name = "orphan.txt";
            var home = MpqHash.GetIndex(name, Mask);
            using var archive = Reopened(Orphan(name, (home + 2) & Mask, 2));

            Assert.IsTrue(archive.GetHashTable()[(int)home].IsDeleted);
            Assert.IsTrue(archive.GetHashTable()[(int)((home + 1) & Mask)].IsDeleted);
            Assert.IsTrue(archive.FileExists(name));
            Assert.AreEqual((int)((home + 2) & Mask), archive.FindHashIndex(name, MpqLocale.Neutral));
        }

        [TestMethod]
        public void TestRebuiltArchiveKeepsTheGamesLookups()
        {
            var (first, second) = Colliding();
            using var originalStream = new MemoryStream();
            using (MpqArchive.Create(originalStream, new[] { File(first), File(second) }, Options(), leaveOpen: true))
            {
            }

            // Opened without a list file, so the second file's name is not known and the builder keeps it at its slot.
            originalStream.Position = 0;
            using var original = MpqArchive.Open(originalStream);
            var builder = new MpqArchiveBuilder(original);
            builder.RemoveFile(first);
            using var rebuiltStream = new MemoryStream();
            builder.SaveTo(rebuiltStream, Options(), leaveOpen: true);

            rebuiltStream.Position = 0;
            using var rebuilt = MpqArchive.Open(rebuiltStream);
            Assert.AreEqual(TableSize, rebuilt.GetHashTable().Count);
            Assert.IsFalse(rebuilt.FileExists(first));
            Assert.IsTrue(rebuilt.FileExists(second));
        }

        [TestMethod]
        public void TestHashEntryPastTheBlockTableIsNoFile()
        {
            using var stream = new MemoryStream();
            using (MpqArchive.Create(stream, new[] { File("a.txt") }, Options(), leaveOpen: true))
            {
            }

            stream.Position = 0;
            using var archive = MpqArchive.Open(stream);

            // An entry whose block index is past the block table, as some maps have: the game finds no file through it.
            var junk = Enumerable.Range(0, 100).Select(i => $"junk{i}.txt").First(name => archive.HashTable[(int)MpqHash.GetIndex(name, Mask)].IsEmpty);
            archive.HashTable[(int)MpqHash.GetIndex(junk, Mask)] = new MpqHash(junk, Mask, MpqLocale.Neutral, 7);

            Assert.IsFalse(archive.FileExists(junk));
            Assert.AreEqual(-1, archive.FindHashIndex(junk, MpqLocale.Neutral));

            using var rebuiltStream = new MemoryStream();
            new MpqArchiveBuilder(archive).SaveTo(rebuiltStream, Options(), leaveOpen: true);

            rebuiltStream.Position = 0;
            using var rebuilt = MpqArchive.Open(rebuiltStream);
            Assert.IsTrue(rebuilt.FileExists("a.txt"));
            Assert.IsFalse(rebuilt.FileExists(junk));
        }

        private static MpqArchiveCreateOptions Options() => new()
        {
            BlockSize = 3,
            HashTableSize = TableSize,
            ListFileCreateMode = MpqFileCreateMode.None,
            AttributesCreateMode = MpqFileCreateMode.None,
        };

        private static MpqFile File(string name) => MpqFile.New(new MemoryStream(new byte[] { 1, 2, 3 }), name);

        private static MpqFile Orphan(string name, uint hashIndex, uint hashCollisions)
        {
            var hash = new MpqHash(name, Mask, MpqLocale.Neutral, 0);
            return MpqFile.New(new MemoryStream(new byte[] { 1, 2, 3 }), hash, hashIndex, hashCollisions);
        }

        private static MpqArchive Reopened(MpqFile file)
        {
            var stream = new MemoryStream();
            using (MpqArchive.Create(stream, new[] { file }, Options(), leaveOpen: true))
            {
            }

            stream.Position = 0;
            return MpqArchive.Open(stream);
        }

        // Two names whose home slot in a table of TableSize slots is the same.
        private static (string First, string Second) Colliding()
        {
            var seen = new Dictionary<uint, string>();
            for (var i = 0; ; i++)
            {
                var name = $"file{i}.txt";
                var home = MpqHash.GetIndex(name, Mask);
                if (seen.TryGetValue(home, out var first))
                {
                    return (first, name);
                }

                seen[home] = name;
            }
        }
    }
}
