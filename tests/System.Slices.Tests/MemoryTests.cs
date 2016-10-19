// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Buffers;
using Xunit;

namespace System.Slices.Tests
{
    public class MemoryTests
    {
        [Fact]
        public void ArrayMemory()
        {
            Memory<byte> copyStoredForLater;

            using (var manager = new OwnedArray<byte>(1024)) {
                Memory<byte> memory = manager.Memory;
                Memory<byte> memorySlice = memory.Slice(10);
                copyStoredForLater = memorySlice;
                using (memorySlice.Reserve()) { // increments the "outstanding span" refcount
                    Assert.Throws<InvalidOperationException>(() => { manager.Dispose(); }); // memory is reserved; cannot dispose
                    Span<byte> span = memorySlice.Span;
                    span[0] = 255;

                    ArraySegment<byte> array;
                    if (!memorySlice.TryGetArray(out array)) {
                        throw new Exception();
                    }
                    if (array.Array[10] != 255) {
                        throw new Exception("array");
                    }
                } // releases the refcount
            }
            Assert.Throws<ObjectDisposedException>(() => { var span = copyStoredForLater.Span; }); // manager is disposed
        }

        [Fact]
        public void NativeMemory()
        {
            Memory<byte> copyStoredForLater;

            using (var manager = new OwnedNativeMemory(1024)) {
                Memory<byte> memory = manager.Memory;
                Memory<byte> memorySlice = memory.Slice(10);
                copyStoredForLater = memorySlice;
                using (memorySlice.Reserve()) {
                    Assert.Throws<InvalidOperationException>(() => { manager.Dispose(); }); // memory is reserved; cannot dispose
                    Span<byte> span = memorySlice.Span;
                    span[0] = 255;

                    unsafe
                    {
                        void* pointer;
                        if (!memory.TryGetPointer(out pointer)) {
                            throw new Exception();
                        }
                        if (((byte*)pointer)[10] != 255) {
                            throw new Exception("native");
                        }
                    }
                }
            }
            Assert.Throws<ObjectDisposedException>(() => { var span = copyStoredForLater.Span; }); // manager is disposed
        }

        [Fact]
        public unsafe void PinnedArrayMemory()
        {
            Memory<byte> copyStoredForLater;

            var bytes = new byte[1024];

            fixed (byte* pBytes = bytes) {
                using (var manager = new OwnedPinnedArray<byte>(bytes, pBytes)) {
                    Memory<byte> memory = manager.Memory;
                    Memory<byte> memorySlice = memory.Slice(10);
                    copyStoredForLater = memorySlice;
                    using (memorySlice.Reserve()) {
                        Assert.Throws<InvalidOperationException>(() => { manager.Dispose(); }); // memory is reserved; cannot dispose

                        Span<byte> span = memorySlice.Span;
                        span[0] = 255;

                        ArraySegment<byte> array;
                        if (!memorySlice.TryGetArray(out array)) {
                            throw new Exception();
                        }
                        if (array.Array[10] != 255) {
                            throw new Exception("pinned");
                        }

                        void* pointer;
                        if (!memory.TryGetPointer(out pointer)) {
                            throw new Exception();
                        }
                        if (((byte*)pointer)[10] != 255) {
                            throw new Exception("pinned");
                        }
                    }
                }   
            }
            Assert.Throws<ObjectDisposedException>(() => { var span = copyStoredForLater.Span; }); // manager is disposed
        }
    }
}