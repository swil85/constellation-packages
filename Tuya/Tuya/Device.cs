using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;


namespace Tuya
{
    /// <summary>
    /// Represent a tuya device
    /// </summary>
    public class TuyaDevice
    {
        /// <summary>
        /// ctor
        /// </summary>
        public TuyaDevice() { }

        /// <summary>
        /// Device ip
        /// </summary>
        public string IP { get; set; }

        /// <summary>
        /// Device id : devId / Virtual ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// localKey 
        /// </summary>
        public string LocalKey { get; set; }

        /// <summary>
        /// Port
        /// </summary>
        public int Port { get; set; } = 6668;

        /// <summary>
        /// Version
        /// </summary>
        public string Version { get; set; } = "3.1";

        /// <summary>
        /// Create request payload
        /// </summary>
        /// <param name="data">data</param>
        /// <param name="encrypt">true if need to encrypt data</param>
        /// <returns>payload</returns>
        protected byte[] CreatePayload(string data, bool encrypt = true)
        {
            if (!encrypt) return Encoding.UTF8.GetBytes(data);

            var bytes = Encoding.UTF8.GetBytes(data);
            var aes = new AesManaged()
            {
                Mode = CipherMode.ECB,
                Key = Encoding.UTF8.GetBytes(LocalKey)
            };
            using (var ms = new MemoryStream())
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(bytes, 0, bytes.Length);
                cs.Close();
                bytes = ms.ToArray(); // encrypt the data
            }
            string data64 = Convert.ToBase64String(bytes);
            byte[] payload = Encoding.UTF8.GetBytes($"data={data64}||lpv={Version}||");
            using (var md5 = MD5.Create())
            using (var ms = new MemoryStream())
            {
                ms.Write(payload, 0, payload.Length);
                ms.Write(aes.Key, 0, aes.Key.Length); // include the local key in the signed data
                string md5s = ArrayToHex(md5.ComputeHash(ms.ToArray())).Substring(8, 16);
                // return data with version & MD5 hash/signature
                return Encoding.UTF8.GetBytes($"{Version}{md5s}{data64}");
            }
        }

        /// <summary>
        /// Send data to device
        /// </summary>
        /// <param name="data">data</param>
        protected async Task<byte[]> Send(byte[] data)
        {
            int tries = 2;
            Exception lastException = null;
            while (tries-- > 0)
            {
                try
                {
                    using (var client = new TcpClient(IP, Port))
                    using (var stream = client.GetStream())
                    using (var ms = new MemoryStream())
                    {
                        byte[] buffer = new byte[1024];
                        await stream.WriteAsync(data, 0, data.Length);
                        int bytes = await stream.ReadAsync(buffer, 0, buffer.Length);
                        stream.Close();
                        ms.Write(buffer, 0, bytes);
                        return ms.ToArray();
                    }
                }
                catch (IOException ex)
                {
                    // sockets sometimes drop the connection unexpectedly, so let's 
                    // retry at least once
                    lastException = ex;
                }
                await Task.Delay(500);
            }
            throw lastException;
        }

        /// <summary>
        /// Read response
        /// </summary>
        /// <param name="data">data</param>
        /// <returns>response</returns>
        protected string ReadBuffer(byte[] data)
        {
            var spec = new
            {
                prefix = new byte[] { 0, 0, 85, 170, 0, 0, 0, 0, 0, 0, 0 },
                suffix = new byte[] { 0, 0, 170, 85 }
            };
            if (data.Length < 24 || !data.Take(11).SequenceEqual(spec.prefix))
            {
                throw new Exception("invalid read buffer/prefix");
            }
            // skip byte 12 (command)
            int length = BitConverter.ToInt32(data.Skip(12).Take(4).Reverse().ToArray(), 0);
            if (data.Length != 16 + length)
            {
                throw new Exception("invalid read buffer length");
            }
            // skip bytes 17-20 (unknown?)
            string payload = Encoding.UTF8.GetString(data.Skip(20).Take(length - 12).ToArray());
            // skip bytes N-8 to N-4 (unknown?)
            if (!data.Skip(16 + length - 4).Take(4).SequenceEqual(spec.suffix))
            {
                throw new Exception("invalid read buffer/suffix");
            }
            return payload;
        }

        /// <summary>
        /// Construct request buffer
        /// </summary>
        /// <param name="data">data</param>
        /// <param name="command">command</param> // TODO ENUM
        /// <returns>bufer</returns>
        protected byte[] ConstructBuffer(byte[] data, byte command)
        {
            var spec = new
            {
                prefix = new byte[] { 0, 0, 85, 170, 0, 0, 0, 0, 0, 0, 0 },
                suffix = new byte[] { 0, 0, 0, 0, 0, 0, 170, 85 }
            };
            byte[] dataLength = BitConverter.GetBytes(data.Length + spec.suffix.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(dataLength); // make big-endian
            using (var ms = new MemoryStream())
            {
                ms.Write(spec.prefix, 0, spec.prefix.Length);
                ms.Write(new byte[] { command }, 0, 1);
                ms.Write(dataLength, 0, 4);
                ms.Write(data, 0, data.Length);
                ms.Write(spec.suffix, 0, spec.suffix.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Convert byte array to string
        /// </summary>
        /// <param name="arr">byte array</param>
        /// <returns>string</returns>
        private static string ArrayToHex(byte[] arr)
        {
            return BitConverter.ToString(arr).Replace("-", string.Empty).ToLower();
        }

        /// <summary>
        /// Convert hex string to byte array
        /// </summary>
        /// <param name="hex">hex string</param>
        /// <returns>byte array</returns>
        private static byte[] HexToArray(string hex)
        {
            return Enumerable.Range(0, hex.Length / 2)
                .Select(x => Convert.ToByte(hex.Substring(x * 2, 2), 16)).ToArray();
        }

        public enum Command
        {
            /// <summary>
            /// Change wifi settings
            /// The payload looks like this: {"ssid":"YourSSID","passwd":"yourpassword","token":""} It needs to be sent in the clear like the status command (10)
            /// </summary>
            ChangeWifiSettings = 1,
            /// <summary>
            /// Send command
            /// </summary>
            SetStatus = 7,
            /// <summary>
            /// Get actual device status
            /// </summary>
            GetStatus = 10,
            /// <summary>
            /// Get available ssid
            /// { ssid_list: [ 'TP-LINK_7F840E', 'BELL392', 'orangeblossom23_RE', 'Euvalyn', 'HP-Print-F4-Photosmart 6520', 'BELL674', 'killer' ] }
            /// </summary>
            GetSSIDList = 11
        }
    }
}