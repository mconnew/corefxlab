﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Buffers
{
    /// <summary>
    /// Used to read text from byte buffers
    /// </summary>
    public struct BytesReader
    {
        readonly SymbolTable _symbolTable;
        ReadOnlyBytes _unreadSegments;
        int _index; // index relative to the begining of bytes passed to the constructor

        ReadOnlyMemory<byte> _currentSegment;
        int _currentSegmentIndex;

        public BytesReader(ReadOnlyBytes bytes, SymbolTable symbolTable)
        {
            _unreadSegments = bytes;
            _currentSegment = bytes.First;
            _symbolTable = symbolTable;
            _currentSegmentIndex = 0;
            _index = 0;
        }

        public BytesReader(ReadOnlyMemory<byte> bytes, SymbolTable symbolTable)
        {
            _unreadSegments = new ReadOnlyBytes(bytes);
            _currentSegment = bytes;
            _symbolTable = symbolTable;
            _currentSegmentIndex = 0;
            _index = 0;
        }

        public BytesReader(ReadOnlyMemory<byte> bytes) : this(bytes, SymbolTable.InvariantUtf8)
        { }

        public BytesReader(ReadOnlyBytes bytes) : this(bytes, SymbolTable.InvariantUtf8)
        { }

        public byte Peek() => _currentSegment.Span[_currentSegmentIndex];

        public ReadOnlySpan<byte> Unread => _currentSegment.Span.Slice(_currentSegmentIndex);

        public SymbolTable SymbolTable => _symbolTable;

        public ReadOnlyBytes? ReadBytesUntil(byte value)
        {
            var index = IndexOf(value);
            if (index == -1) return null;
            return ReadBytes(index);
        }
        public ReadOnlyBytes? ReadBytesUntil(ReadOnlySpan<byte> value)
        {
            var index = IndexOf(value);
            if (index == -1) return null;
            return ReadBytes(index);
        }

        public Range? ReadRangeUntil(byte value)
        {
            var index = IndexOf(value);
            if (index == -1) return null;
            return ReadRange(index);
        }
        public Range? ReadRangeUntil(ReadOnlySpan<byte> values)
        {
            var index = IndexOf(values);
            if (index == -1) return null;
            return ReadRange(index);
        }

        public ReadOnlyBytes ReadBytes(int count)
        {
            var result = _unreadSegments.Slice(_currentSegmentIndex, count);
            Advance(count);
            return result;
        }
        public Range ReadRange(int count)
        {
            var result = new Range(_index, count);
            Advance(count);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf(byte value)
        {
            var index = Unread.IndexOf(value);
            if (index != -1)
            {
                return index;
            }

            var indexOfRest = IndexOfRest(value);
            if (indexOfRest == -1) return -1;
            return indexOfRest + _currentSegment.Length - _currentSegmentIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf(ReadOnlySpan<byte> values)
        {
            var unread = Unread;
            var index = unread.IndexOf(values);
            if (index != -1)
            {
                return index;
            }

            return IndexOfStraddling(values);
        }

        public bool IsEmpty
        {
            get {
                if (_currentSegment.Length - _currentSegmentIndex > 0)
                {
                    return false;
                }
                AdvanceNextSegment(0, 0);
                if (_currentSegment.Length == 0) return true;
                return IsEmpty;
            }
        }

        public int Index => _index;

        int CopyTo(Span<byte> buffer)
        {
            var first = Unread;
            if (first.Length > buffer.Length)
            {
                first.Slice(0, buffer.Length).CopyTo(buffer);
                return buffer.Length;
            }
            if (first.Length == buffer.Length)
            {
                first.CopyTo(buffer);
                return buffer.Length;
            }

            first.CopyTo(buffer);
            var rest = _unreadSegments.Rest;
            if (rest == null) return first.Length;
            return first.Length + rest.CopyTo(buffer.Slice(first.Length));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(int count)
        {
            var unreadLength = _currentSegment.Length - _currentSegmentIndex;
            if (count < unreadLength)
            {
                _currentSegmentIndex += count;
                _index += count;
            }
            else
            {
                AdvanceNextSegment(count, advancedInCurrent:unreadLength);
            }
        }

        void AdvanceNextSegment(int count, int advancedInCurrent)
        {
            Debug.Assert(advancedInCurrent == _currentSegment.Length - _currentSegmentIndex);
            var newUnreadSegments = _unreadSegments.Rest;
            var toAdvanceUnreadSegments = count - advancedInCurrent;

            if (newUnreadSegments == null) { 
                if (toAdvanceUnreadSegments == 0)
                {
                    _unreadSegments = ReadOnlyBytes.Empty;
                    _currentSegment = ReadOnlyMemory<byte>.Empty;
                    _currentSegmentIndex = 0;
                    _index += count;
                    return;
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }
            }

            _currentSegment = newUnreadSegments.First;
            _currentSegmentIndex = 0;
            _unreadSegments = _unreadSegments.Slice(_unreadSegments.First.Length);
            _index += advancedInCurrent;

            if (toAdvanceUnreadSegments != 0)
            {
                Advance(toAdvanceUnreadSegments); // TODO: this recursive implementation could be optimized
            }
        }

        int IndexOf(IReadOnlyBufferList<byte> sequence, byte value)
        {
            if (sequence == null) return -1;
            var first = sequence.First;
            var index = first.Span.IndexOf(value);
            if (index > -1) return index;

            var indexOfRest = IndexOf(sequence.Rest, value);
            if (indexOfRest < 0) return -1;
            return first.Length + indexOfRest;
        }

        int IndexOfStraddling(ReadOnlySpan<byte> value)
        {
            var rest = _unreadSegments.Rest;
            if (rest == null) return -1;
            ReadOnlySpan<byte> unread = Unread;
            int index = 0;
            // try to find the bytes straddling _first and _rest
            int bytesToSkipFromFirst = 0; // these don't have to be searched again
            if (unread.Length > value.Length - 1)
            {
                bytesToSkipFromFirst = unread.Length - value.Length - 1;
            }
            ReadOnlySpan<byte> bytesToSearchAgain;
            if (bytesToSkipFromFirst > 0)
            {
                bytesToSearchAgain = unread.Slice(bytesToSkipFromFirst);
            }
            else
            {
                bytesToSearchAgain = unread;
            }

            if (bytesToSearchAgain.IndexOf(value[0]) != -1)
            {
                var tempLen = value.Length << 1;
                var tempSpan = tempLen < 128 ?
                                        stackalloc byte[tempLen] :
                                        // TODO (pri 3): I think this could be imporved by chunking values
                                        new byte[tempLen];

                bytesToSearchAgain.CopyTo(tempSpan);
                rest.CopyTo(tempSpan.Slice(bytesToSearchAgain.Length));
                index = tempSpan.IndexOf(value);
                if (index != -1)
                {
                    return index + bytesToSkipFromFirst;
                }
            }

            // try to find the bytes in _rest
            index = rest.IndexOf(value);
            if (index != -1) return unread.Length + index;

            return -1;
        }

        int IndexOfRest(byte value)
        {
            var index = IndexOf(_unreadSegments.Rest, value);
            if (index == -1)
            {
                return -1;
            }
            return index;
        }

        #region Parsing Methods
        // TODO: how to we chose the lengths of the temp buffers?
        // TODO: these methods call the slow overloads of Parsers.Custom.TryParseXXX. Do we need fast path?
        // TODO: these methods hardcode the format. Do we need this to be something that can be specified?
        public bool TryParseBoolean(out bool value)
        {
            int consumed;
            var unread = Unread;
            if (CustomParser.TryParseBoolean(unread, out value, out consumed, _symbolTable))
            {
                Debug.Assert(consumed <= unread.Length);
                if (unread.Length > consumed)
                {
                    _currentSegmentIndex += consumed;
                    _index += consumed;
                    return true;
                }
            }

            Span<byte> tempSpan = stackalloc byte[15];
            var copied = CopyTo(tempSpan);

            if (CustomParser.TryParseBoolean(tempSpan.Slice(0, copied), out value, out consumed, _symbolTable))
            {
                Advance(consumed);
                return true;
            }

            return false;
        }

        public bool TryParseUInt64(out ulong value)
        {
            int consumed;
            var unread = Unread;
            if (CustomParser.TryParseUInt64(unread, out value, out consumed, default, _symbolTable))
            {
                if (unread.Length > consumed)
                {
                    _currentSegmentIndex += consumed;
                    _index += consumed;
                    return true;
                }
            }

            Span<byte> tempSpan = stackalloc byte[32];
            var copied = CopyTo(tempSpan);

            if (CustomParser.TryParseUInt64(tempSpan.Slice(0, copied), out value, out consumed, 'G', _symbolTable))
            {
                Advance(consumed);
                return true;
            }

            return false;
        }
        #endregion
    }
}
