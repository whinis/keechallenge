﻿/* KeeChallenge--Provides Yubikey challenge-response capability to Keepass
*  Copyright (C) 2014  Ben Rush
*  
*  This program is free software; you can redistribute it and/or
*  modify it under the terms of the GNU General Public License
*  as published by the Free Software Foundation; either version 2
*  of the License, or (at your option) any later version.
*  
*  This program is distributed in the hope that it will be useful,
*  but WITHOUT ANY WARRANTY; without even the implied warranty of
*  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
*  GNU General Public License for more details.
*  
*  You should have received a copy of the GNU General Public License
*  along with this program; if not, write to the Free Software
*  Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
*/

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Forms;
using System.Security;
using System.Runtime.ConstrainedExecution;
using System.IO;

namespace KeeChallenge
{
    public enum YubiSlot
    {
        AUTO = 0,
        SLOT1 = 1,
        SLOT2 = 2,
    };

    public class YubiWrapper
    {
        public const uint yubiRespLen = 20;
        private const uint yubiBuffLen = 64;

        private List<string> nativeDLLs =  new List<string>() { "libykpers-1-1.dll", "libyubikey-0.dll", "libjson-0.dll", "libjson-c-2.dll" };

        private static bool is64BitProcess = (IntPtr.Size == 8);

        private static bool IsLinux
        {
            get
            {
                int p = (int)Environment.OSVersion.Platform;
                return (p == 4) || (p == 6) || (p == 128);
            }
        }

        public static string AssemblyDirectory
        {
            get
            {
                string codeBase = System.Reflection.Assembly.GetEntryAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string methodName);

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail), DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("libykpers-1-1")]
        private static extern int yk_init();


        [DllImport("libykpers-1-1")]
        private static extern int yk_release();


        [DllImport("libykpers-1-1")]
        private static extern int yk_close_key(IntPtr yk);


        [DllImport("libykpers-1-1")]
        private static extern IntPtr yk_open_first_key();

        [DllImport("libykpers-1-1")]
        private static extern IntPtr yk_open_key_vid_pid(int vid, IntPtr pids, UIntPtr pids_len, int index);

        [DllImport("libykpers-1-1")]
        private static extern int yk_challenge_response(IntPtr yk, byte yk_cmd, int may_block, uint challenge_len, byte[] challenge, uint response_len, byte[] response);
             

        [SecurityCritical]
        internal static bool DoesWin32MethodExist(string moduleName, string methodName)
        {
            IntPtr moduleHandle = GetModuleHandle(moduleName);
            if (moduleHandle == IntPtr.Zero)
            {
                return false;
            }
            return (GetProcAddress(moduleHandle, methodName) != IntPtr.Zero);
        }
        
        private static ReadOnlyCollection<byte> slots = new ReadOnlyCollection<byte>(new List<byte>()
        {
            0x30, //SLOT_CHAL_HMAC1
            0x38  //SLOT_CHAL_HMAC2
        });

        private IntPtr yk = IntPtr.Zero;
        private bool m_onlyKey = false;

        public bool Init()
        {
            try
            { 
                if (!IsLinux) //no DLL Hell on Linux!
                {     
                    foreach (string s in nativeDLLs) //support upgrading from installs of versions 1.0.2 and prior
                    {
                        string path = Path.Combine(Environment.CurrentDirectory, s);
                        if (File.Exists(path)) //prompt the user to do it to avoid permissions issues
                        {
                            try
                            {
                                File.Delete(path);
                            }
                            catch (Exception)
                            {
                                string warn = "Please login as an administrator and delete the following files from " + Environment.CurrentDirectory + ":\n" + string.Join("\n", nativeDLLs.ToArray());
                                MessageBox.Show(warn);
                                return false;
                            }
                        }
                    }


                    if (!DoesWin32MethodExist("kernel32.dll", "SetDllDirectoryW")) throw new PlatformNotSupportedException("KeeChallenge requires Windows XP Service Pack 1 or greater");
                    
                    string _32BitDir = Path.Combine(AssemblyDirectory, "32bit");
                    string _64BitDir = Path.Combine(AssemblyDirectory, "64bit");
                    if (!is64BitProcess) 
                        SetDllDirectory(_32BitDir);
                    else
                        SetDllDirectory(_64BitDir);
                }
                if (yk_init() != 1) return false;
                yk = yk_open_first_key();
                if (yk == IntPtr.Zero) //if its false attempt onlykey
                {
                    int[] device_pids = { 0x60fc }; // OnlyKey PID
                    GCHandle handle = GCHandle.Alloc(device_pids, GCHandleType.Pinned);
                    try
                    {
                        IntPtr pointer = handle.AddrOfPinnedObject();
                        yk = yk_open_key_vid_pid(0x1d50, pointer, new UIntPtr(1), 0);
                        m_onlyKey = true;
                    }
                    finally
                    {
                        if (handle.IsAllocated)
                        {
                            handle.Free();
                        }
                    }

                }
                if (yk == IntPtr.Zero) return false; //if still false then return false
            }
            catch (Exception e)
            {
				Console.WriteLine(e.ToString());
				Debug.Assert(false,e.Message);         
                MessageBox.Show("Error connecting to yubikey in init!", "Error", MessageBoxButtons.OK);               
                return false;
            }
           return true;
        }

        public int DetectSlot()
        {
            // Get the correct slot if possible
            Random rnd = new Random();
            for (int i = 1; i <= 2; i++)
            {
                byte[] challenge = new byte[1];
                rnd.NextBytes(challenge);
                byte[] temp = new byte[yubiBuffLen];
                int ret = yk_challenge_response(yk, slots[i-1], 0, 1, challenge, yubiBuffLen, temp);
                
                if (ret == 2)
                {
                    System.Threading.Thread.Sleep(100);
                    ret = yk_challenge_response(yk, slots[i], 0, 1, challenge, yubiBuffLen, temp);
                }
                if (ret != 2 && ret != -1)
                {
                    return i;
                }
            }
            return 0;
        }
  
        public bool ChallengeResponse(int slot, byte[] challenge, out byte[] response)
        {
            response = new byte[yubiRespLen];
            if (yk == IntPtr.Zero) return false;
            
            byte[] temp = new byte[yubiBuffLen];
            int ret = yk_challenge_response(yk, slots[slot], 1, (uint)challenge.Length, challenge, yubiBuffLen, temp);
            if (ret == 1)
            {
                Array.Copy(temp, response, response.Length);
                return true;
            }
            else return false;
        }

        public void Close()
        {
            if (yk != IntPtr.Zero)
            {
                bool ret = YubiWrapper.yk_close_key(yk) == 1;
                if (!ret || YubiWrapper.yk_release() != 1)
                {
                    throw new Exception("Error closing Yubikey");
                }
            }
        }
    }
}
