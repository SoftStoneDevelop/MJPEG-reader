using System;
using ClientMJPEG;
using NUnit.Framework;

namespace ClientMJPEGTests
{
    public class Tests
    {
        [Test]
        public void FindBytesIndexTest()
        {
            var memory = new Memory<byte>(new byte[] { 12, 234, 14, 67, 0, 0, 0, 0, 0, 0, 0, 46, 4, 87, 78, 142, 21, 5, 213, 46, 123, 12, 54, 5, 74, 98 });
            
            Assert.AreEqual(11, memory.FindBytesIndex(new Memory<byte>(new byte[]{ 46, 4, 87 })));
            Assert.AreEqual(-1, memory.FindBytesIndex(new Memory<byte>(new byte[] { 98, 4, 87 })));
            Assert.AreEqual(-1, memory.FindBytesIndex(new Memory<byte>(new byte[] { 12, 234, 14, 67, 0, 0, 0, 0, 0, 0, 0, 46, 4, 87, 78, 142, 21, 5, 213, 46, 123, 12, 54, 5, 74, 98, 49 })));
            Assert.AreEqual(0, memory.FindBytesIndex(new Memory<byte>(new byte[] { 12 })));
            Assert.AreEqual(25, memory.FindBytesIndex(new Memory<byte>(new byte[] { 98 })));
        }
    }
}