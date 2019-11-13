/*
 * Copyright (c) 2015, 2018 Scott Bennett
 *           (c) 2018 Kaarlo Räihä
 *			 (c) 2019 Christian Albrecht
 *
 * Permission to use, copy, modify, and distribute this software for any
 * purpose with or without fee is hereby granted, provided that the above
 * copyright notice and this permission notice appear in all copies.
 *
 * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
 * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
 * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
 * ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
 * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
 * ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
 * OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
 */

using System;
using System.Text;
using System.Runtime.CompilerServices; // For MethodImplOptions.AggressiveInlining

namespace CSChaCha20
{
    public class ChaCha20
    {
        private const int allowedKeyLength = 32;
        private const int processBytesAtTime = 64;
        private const int stateLength = 16;

        private uint[] state = new uint[stateLength];

        private byte[] Key { get; }
        private byte[] Nonce { get; }
        private uint Counter { get; }

        /// <summary>
        /// Set up a new ChaCha20 state. The lengths of the given parameters are checked before encryption happens.
        /// </summary>
        /// <remarks>
        /// See <a href="https://tools.ietf.org/html/rfc7539#page-10">ChaCha20 Spec Section 2.4</a> for a detailed description of the inputs.
        /// </remarks>
        /// <param name="key">
        /// A 32-byte (256-bit) key, treated as a concatenation of eight 32-bit little-endian integers
        /// </param>
        /// <param name="nonce">
        /// A 12-byte (96-bit) nonce, treated as a concatenation of three 32-bit little-endian integers
        /// </param>
        /// <param name="counter">
        /// A 4-byte (32-bit) block counter, treated as a 32-bit little-endian integer
        /// </param>
        public ChaCha20(byte[] key, byte[] nonce, uint counter)
        {
            Key = key;
            Nonce = nonce;
            Counter = counter;
        }

        private void Init()
        {
            KeySetup(Key);
            IvSetup(Nonce, Counter);
        }

        // These are the same constants defined in the reference implementation.
        // http://cr.yp.to/streamciphers/timings/estreambench/submissions/salsa20/chacha8/ref/chacha.c
        private static readonly byte[] sigma = Encoding.ASCII.GetBytes("expand 32-byte k");
        private static readonly byte[] tau = Encoding.ASCII.GetBytes("expand 16-byte k");

        /// <summary>
        /// Set up the ChaCha state with the given key. A 32-byte key is required and enforced.
        /// </summary>
        /// <param name="key">
        /// A 32-byte (256-bit) key, treated as a concatenation of eight 32-bit little-endian integers
        /// </param>
        private void KeySetup(byte[] key)
        {
            if (key == null)
            {
                throw new ArgumentNullException("Key is null");
            }

            if (key.Length != allowedKeyLength)
            {
                throw new ArgumentException($"Key length must be {allowedKeyLength}. Actual: {key.Length}");
            }

            state[4] = BitConverter.ToUInt32(key, 0);
            state[5] = BitConverter.ToUInt32(key, 4);
            state[6] = BitConverter.ToUInt32(key, 8);
            state[7] = BitConverter.ToUInt32(key, 12);

            byte[] constants = (key.Length == allowedKeyLength) ? sigma : tau;
            int keyIndex = key.Length - 16;

            state[8] = BitConverter.ToUInt32(key, keyIndex + 0);
            state[9] = BitConverter.ToUInt32(key, keyIndex + 4);
            state[10] = BitConverter.ToUInt32(key, keyIndex + 8);
            state[11] = BitConverter.ToUInt32(key, keyIndex + 12);

            state[0] = BitConverter.ToUInt32(constants, 0);
            state[1] = BitConverter.ToUInt32(constants, 4);
            state[2] = BitConverter.ToUInt32(constants, 8);
            state[3] = BitConverter.ToUInt32(constants, 12);
        }

        /// <summary>
        /// Set up the ChaCha state with the given nonce (aka Initialization Vector or IV) and block counter. A 12-byte nonce and a 4-byte counter are required.
        /// </summary>
        /// <param name="nonce">
        /// A 12-byte (96-bit) nonce, treated as a concatenation of three 32-bit little-endian integers
        /// </param>
        /// <param name="counter">
        /// A 4-byte (32-bit) block counter, treated as a 32-bit little-endian integer
        /// </param>
        private void IvSetup(byte[] nonce, uint counter)
        {
            if (nonce == null)
            {
                // There has already been some state set up. Clear it before exiting.
                // Dispose();
                throw new ArgumentNullException("Nonce is null");
            }

            if (nonce.Length != 8 && nonce.Length != 12)
            {
                // There has already been some state set up. Clear it before exiting.
                // Dispose();
                throw new ArgumentException($"Nonce length must be 8 or 12. Actual: {nonce.Length}");
            }

            if (nonce.Length == 12)
            {
                state[12] = counter;
                state[13] = BitConverter.ToUInt32(nonce,0);
                state[14] = BitConverter.ToUInt32(nonce, 4);
                state[15] = BitConverter.ToUInt32(nonce, 8);
            }
            if (nonce.Length == 8)
            {
                state[12] = counter;
                state[13] = 0;
                state[14] = BitConverter.ToUInt32(nonce, 0);
                state[15] = BitConverter.ToUInt32(nonce, 4);
            }
        }


        /// <summary>
        /// Encrypt/Decrypt arbitrary-length byte array (input), writing the resulting byte array that is allocated by method.
        /// </summary>
        /// <remarks>Since this is symmetric operation, it doesn't really matter if you use Encrypt or Decrypt method</remarks>
        /// <param name="input">Input byte array</param>
        /// <returns>Byte array that contains encrypted bytes</returns>
        public byte[] CryptBytes(byte[] input)
        {
            Init();

            byte[] returnArray = new byte[input.Length];
            WorkBytes(returnArray, input, input.Length);
            return returnArray;
        }

        /// <summary>
        /// Encrypt or decrypt an arbitrary-length byte array (input), writing the resulting byte array to the output buffer. The number of bytes to read from the input buffer is determined by numBytes.
        /// </summary>
        /// <param name="output"></param>
        /// <param name="input"></param>
        /// <param name="numBytes"></param>
        private void WorkBytes(byte[] output, byte[] input, int numBytes)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input), "Input cannot be null");
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output), "Output cannot be null");
            }

            if (numBytes < 0 || numBytes > input.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(numBytes), "The number of bytes to read must be between [0..input.Length]");
            }

            if (output.Length < numBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(output), $"Output byte array should be able to take at least {numBytes}");
            }

            uint[] x = new uint[stateLength];    // Working buffer
            byte[] tmp = new byte[processBytesAtTime];  // Temporary buffer
            int outputOffset = 0;
            int inputOffset = 0;

            while (numBytes > 0)
            {
                // Copy state to working buffer
                Buffer.BlockCopy(this.state, 0, x, 0, stateLength * sizeof(uint));

                for (int i = 0; i < 10; i++)
                {
                    QuarterRound(x, 0, 4, 8, 12);
                    QuarterRound(x, 1, 5, 9, 13);
                    QuarterRound(x, 2, 6, 10, 14);
                    QuarterRound(x, 3, 7, 11, 15);

                    QuarterRound(x, 0, 5, 10, 15);
                    QuarterRound(x, 1, 6, 11, 12);
                    QuarterRound(x, 2, 7, 8, 13);
                    QuarterRound(x, 3, 4, 9, 14);
                }

                for (int i = 0; i < stateLength; i++)
                {
                    Util.ToBytes(tmp, Util.Add(x[i], this.state[i]), 4 * i);
                }

                this.state[12] = Util.AddOne(state[12]);
                if (this.state[12] <= 0)
                {
                    /* Stopping at 2^70 bytes per nonce is the user's responsibility */
                    this.state[13] = Util.AddOne(state[13]);
                }

                // In case these are last bytes
                if (numBytes <= processBytesAtTime)
                {
                    for (int i = 0; i < numBytes; i++)
                    {
                        output[i + outputOffset] = (byte)(input[i + inputOffset] ^ tmp[i]);
                    }

                    return;
                }

                for (int i = 0; i < processBytesAtTime; i++)
                {
                    output[i + outputOffset] = (byte)(input[i + inputOffset] ^ tmp[i]);
                }

                numBytes -= processBytesAtTime;
                outputOffset += processBytesAtTime;
                inputOffset += processBytesAtTime;
            }
        }

        /// <summary>
        /// The ChaCha Quarter Round operation. It operates on four 32-bit unsigned integers within the given buffer at indices a, b, c, and d.
        /// </summary>
        /// <remarks>
        /// The ChaCha state does not have four integer numbers: it has 16. So the quarter-round operation works on only four of them -- hence the name. Each quarter round operates on four predetermined numbers in the ChaCha state.
        /// See <a href="https://tools.ietf.org/html/rfc7539#page-4">ChaCha20 Spec Sections 2.1 - 2.2</a>.
        /// </remarks>
        /// <param name="x">A ChaCha state (vector). Must contain 16 elements.</param>
        /// <param name="a">Index of the first number</param>
        /// <param name="b">Index of the second number</param>
        /// <param name="c">Index of the third number</param>
        /// <param name="d">Index of the fourth number</param>
        private static void QuarterRound(uint[] x, uint a, uint b, uint c, uint d)
        {
            x[a] = Util.Add(x[a], x[b]);
            x[d] = Util.Rotate(Util.XOr(x[d], x[a]), 16);

            x[c] = Util.Add(x[c], x[d]);
            x[b] = Util.Rotate(Util.XOr(x[b], x[c]), 12);

            x[a] = Util.Add(x[a], x[b]);
            x[d] = Util.Rotate(Util.XOr(x[d], x[a]), 8);

            x[c] = Util.Add(x[c], x[d]);
            x[b] = Util.Rotate(Util.XOr(x[b], x[c]), 7);
        }
    }

    public static class Util
    {
        /// <summary>
        /// n-bit left rotation operation (towards the high bits) for 32-bit integers.
        /// </summary>
        /// <param name="v"></param>
        /// <param name="c"></param>
        /// <returns>The result of (v LEFTSHIFT c)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Rotate(uint v, int c)
        {
            unchecked
            {
                return (v << c) | (v >> (32 - c));
            }
        }

        /// <summary>
        /// Unchecked integer exclusive or (XOR) operation.
        /// </summary>
        /// <param name="v"></param>
        /// <param name="w"></param>
        /// <returns>The result of (v XOR w)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint XOr(uint v, uint w)
        {
            return unchecked(v ^ w);
        }

        /// <summary>
        /// Unchecked integer addition. The ChaCha spec defines certain operations to use 32-bit unsigned integer addition modulo 2^32.
        /// </summary>
        /// <remarks>
        /// See <a href="https://tools.ietf.org/html/rfc7539#page-4">ChaCha20 Spec Section 2.1</a>.
        /// </remarks>
        /// <param name="v"></param>
        /// <param name="w"></param>
        /// <returns>The result of (v + w) modulo 2^32</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Add(uint v, uint w)
        {
            return unchecked(v + w);
        }

        /// <summary>
        /// Add 1 to the input parameter using unchecked integer addition. The ChaCha spec defines certain operations to use 32-bit unsigned integer addition modulo 2^32.
        /// </summary>
        /// <remarks>
        /// See <a href="https://tools.ietf.org/html/rfc7539#page-4">ChaCha20 Spec Section 2.1</a>.
        /// </remarks>
        /// <param name="v"></param>
        /// <returns>The result of (v + 1) modulo 2^32</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint AddOne(uint v)
        {
            return unchecked(v + 1);
        }

        /// <summary>
        /// Serialize the input integer into the output buffer. The input integer will be split into 4 bytes and put into four sequential places in the output buffer, starting at the outputOffset.
        /// </summary>
        /// <param name="output"></param>
        /// <param name="input"></param>
        /// <param name="outputOffset"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToBytes(byte[] output, uint input, int outputOffset)
        {
            unchecked
            {
                output[outputOffset] = (byte)input;
                output[outputOffset + 1] = (byte)(input >> 8);
                output[outputOffset + 2] = (byte)(input >> 16);
                output[outputOffset + 3] = (byte)(input >> 24);
            }
        }
    }
}
