﻿using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Furari.Text {

    /// <summary>
    /// This imitates .NET's internal ValueStringBuilder but always rents array from ArrayPool.
    /// </summary>
    public ref partial struct ValueStringBuilder {
        private char[] _arrayToReturnToPool;
        private Span<char> _chars;
        private int _pos;

        public ValueStringBuilder(int initialCapacity) {
            _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(initialCapacity);
            _chars = _arrayToReturnToPool;
            _pos = 0;
        }

        public int Length {
            get => _pos;
            set {
                Debug.Assert(value >= 0);
                Debug.Assert(value <= _chars.Length);
                _pos = value;
            }
        }

        public int Capacity => _chars.Length;

        public void Clear() {
            _pos = 0;
        }

        public void EnsureCapacity(int capacity) {
            // This is not expected to be called this with negative capacity
            Debug.Assert(capacity >= 0);

            // If the caller has a bug and calls this with negative capacity, make sure to call Grow to throw an exception.
            if ((uint)capacity > (uint)_chars.Length)
                Grow(capacity - _pos);
        }

        /// <summary>
        /// Get a pinnable reference to the builder.
        /// Does not ensure there is a null char after <see cref="Length"/>
        /// This overload is pattern matched in the C# 7.3+ compiler so you can omit
        /// the explicit method call, and write eg "fixed (char* c = builder)"
        /// </summary>
        public ref char GetPinnableReference() {
            return ref MemoryMarshal.GetReference(_chars);
        }

        /// <summary>
        /// Get a pinnable reference to the builder.
        /// </summary>
        /// <param name="terminate">Ensures that the builder has a null char after <see cref="Length"/></param>
        public ref char GetPinnableReference(bool terminate) {
            if (terminate) {
                EnsureCapacity(Length + 1);
                _chars[Length] = '\0';
            }
            return ref MemoryMarshal.GetReference(_chars);
        }

        public ref char this[int index] {
            get {
                Debug.Assert(index < _pos);
                return ref _chars[index];
            }
        }

        public override string ToString() {
            string s = _chars.Slice(0, _pos).ToString();
            Dispose();
            return s;
        }

        /// <summary>Returns the underlying storage of the builder.</summary>
        public Span<char> RawChars => _chars;

        /// <summary>
        /// Returns a span around the contents of the builder.
        /// </summary>
        /// <param name="terminate">Ensures that the builder has a null char after <see cref="Length"/></param>
        public ReadOnlySpan<char> AsSpan(bool terminate) {
            if (terminate) {
                EnsureCapacity(Length + 1);
                _chars[Length] = '\0';
            }
            return _chars.Slice(0, _pos);
        }

        public ReadOnlySpan<char> AsSpan() => _chars.Slice(0, _pos);
        public ReadOnlySpan<char> AsSpan(int start) => _chars.Slice(start, _pos - start);
        public ReadOnlySpan<char> AsSpan(int start, int length) => _chars.Slice(start, length);

        public bool TryCopyTo(Span<char> destination, out int charsWritten) {
            if (_chars.Slice(0, _pos).TryCopyTo(destination)) {
                charsWritten = _pos;
                Dispose();
                return true;
            } else {
                charsWritten = 0;
                Dispose();
                return false;
            }
        }

        public void Insert(int index, char value, int count) {
            if (_pos > _chars.Length - count) {
                Grow(count);
            }

            int remaining = _pos - index;
            _chars.Slice(index, remaining).CopyTo(_chars.Slice(index + count));
            _chars.Slice(index, count).Fill(value);
            _pos += count;
        }

        public void Insert(int index, string s) {
            if (s == null) {
                return;
            }

            int count = s.Length;

            if (_pos > (_chars.Length - count)) {
                Grow(count);
            }

            int remaining = _pos - index;
            _chars.Slice(index, remaining).CopyTo(_chars.Slice(index + count));
            s.AsSpan().CopyTo(_chars.Slice(index));
            _pos += count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(char c) {
            int pos = _pos;
            Span<char> chars = _chars;
            if ((uint)pos >= (uint)chars.Length) {
                Grow(1);
            }

            chars[pos] = c;
            _pos = pos + 1;

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(string s) {
            if (s == null) {
                return;
            }

            int pos = _pos;
            if (s.Length == 1 && (uint)pos < (uint)_chars.Length) { // very common case, e.g. appending strings from NumberFormatInfo like separators, percent symbols, etc.
                _chars[pos] = s[0];
                _pos = pos + 1;
            } else {
                AppendSlow(s);
            }
        }

        private void AppendSlow(string s) {
            int pos = _pos;
            if (pos > _chars.Length - s.Length) {
                Grow(s.Length);
            }

            s.AsSpan().CopyTo(_chars.Slice(pos));
            _pos += s.Length;
        }

        public void Append(char c, int count) {
            if (_pos > _chars.Length - count) {
                Grow(count);
            }

            Span<char> dst = _chars.Slice(_pos, count);
            for (int i = 0; i < dst.Length; i++) {
                dst[i] = c;
            }
            _pos += count;
        }

        public void Append(ReadOnlySpan<char> value) {
            int pos = _pos;
            if (pos > _chars.Length - value.Length) {
                Grow(value.Length);
            }

            value.CopyTo(_chars.Slice(_pos));
            _pos += value.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<char> AppendSpan(int length) {
            int origPos = _pos;
            if (origPos > _chars.Length - length) {
                Grow(length);
            }

            _pos = origPos + length;
            return _chars.Slice(origPos, length);
        }

        /// <summary>
        /// Resize the internal buffer either by doubling current buffer size or
        /// by adding <paramref name="additionalCapacityBeyondPos"/> to
        /// <see cref="_pos"/> whichever is greater.
        /// </summary>
        /// <param name="additionalCapacityBeyondPos">
        /// Number of chars requested beyond current position.
        /// </param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow(int additionalCapacityBeyondPos) {
            Debug.Assert(additionalCapacityBeyondPos > 0);
            Debug.Assert(_pos > _chars.Length - additionalCapacityBeyondPos, "Grow called incorrectly, no resize is needed.");

            const uint ArrayMaxLength = 0x7FFFFFC7; // same as Array.MaxLength

            // Increase to at least the required size (_pos + additionalCapacityBeyondPos), but try
            // to double the size if possible, bounding the doubling to not go beyond the max array length.
            int newCapacity = (int)Math.Max(
                (uint)(_pos + additionalCapacityBeyondPos),
                Math.Min((uint)_chars.Length * 2, ArrayMaxLength));

            // Make sure to let Rent throw an exception if the caller has a bug and the desired capacity is negative.
            // This could also go negative if the actual required length wraps around.
            char[] poolArray = ArrayPool<char>.Shared.Rent(newCapacity);

            _chars.Slice(0, _pos).CopyTo(poolArray);

            char[] toReturn = _arrayToReturnToPool;
            _chars = _arrayToReturnToPool = poolArray;
            if (toReturn != null) {
                ArrayPool<char>.Shared.Return(toReturn);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() {
            char[] toReturn = _arrayToReturnToPool;
            this = default; // for safety, to avoid using pooled array if this instance is erroneously appended to again
            if (toReturn != null) {
                ArrayPool<char>.Shared.Return(toReturn);
            }
        }
    }


    public partial struct ValueStringBuilder {

        private Span<char> Allocate(int length) {
            EnsureCapacity(Length + length);
            return _chars.Slice(_pos, length);
        }

        public void Append(int value) {
            var buffer = Allocate(11);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(uint value) {
            var buffer = Allocate(10);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(long value) {
            var buffer = Allocate(20);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(ulong value) {
            var buffer = Allocate(20);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(short value) {
            var buffer = Allocate(6);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(ushort value) {
            var buffer = Allocate(5);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(byte value) {
            var buffer = Allocate(3);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(sbyte value) {
            var buffer = Allocate(4);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(float value) {
            var buffer = Allocate(32);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(double value) {
            var buffer = Allocate(32);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(decimal value) {
            var buffer = Allocate(32);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(bool value) {
            var buffer = Allocate(5);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(DateTime value) {
            var buffer = Allocate(32);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(Guid value) {
            var buffer = Allocate(36);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        public void Append(TimeSpan value) {
            var buffer = Allocate(16);
            value.TryFormat(buffer, out int charWritten);
            _pos += charWritten;
        }

        //---------------------------------------------

        public void AppendFormat(float value, ReadOnlySpan<char> format = default, IFormatProvider provider = null) {
            var buffer = Allocate(32);
            if (!value.TryFormat(buffer, out int charWritten, format, provider)) {
                buffer = Allocate(48);
                if (!value.TryFormat(buffer, out charWritten, format, provider)) { throw new FormatException(); }
            }
            _pos += charWritten;
        }

        public void AppendFormat(double value, ReadOnlySpan<char> format = default, IFormatProvider provider = null) {
            var buffer = Allocate(32);
            if (!value.TryFormat(buffer, out int charWritten, format, provider)) {
                buffer = Allocate(48);
                if (!value.TryFormat(buffer, out charWritten, format, provider)) { throw new FormatException(); }
            }
            _pos += charWritten;
        }

        public void AppendFormat(DateTime value, ReadOnlySpan<char> format = default, IFormatProvider provider = null) {
            var buffer = Allocate(32);
            if (!value.TryFormat(buffer, out int charWritten, format, provider)) {
                buffer = Allocate(64);
                if (!value.TryFormat(buffer, out charWritten, format, provider)) { throw new FormatException(); }
            }
            _pos += charWritten;
        }

        public void AppendFormat(TimeSpan value, ReadOnlySpan<char> format = default, IFormatProvider provider = null) {
            var buffer = Allocate(16);
            if (!value.TryFormat(buffer, out int charWritten, format, provider)) {
                buffer = Allocate(32);
                if (!value.TryFormat(buffer, out charWritten, format, provider)) { throw new FormatException(); }
            }
            _pos += charWritten;
        }
    }


}