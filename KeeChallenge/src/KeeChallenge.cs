/* KeeChallenge--Provides Yubikey challenge-response capability to Keepass
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
using System.IO;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Xml;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections;

using KeePassLib.Keys;
using KeePassLib.Utility;
using KeePass.Plugins;
using KeePassLib.Cryptography;
using KeePassLib.Serialization;

namespace KeeChallenge
{
    public sealed class KeeChallengeProv : KeyProvider
    {
        public const string m_name = "Yubikey challenge-response";
        public const int keyLenBytes = 20;
        public const int challengeLenBytes = 64;
        public const int secretLenBytes = 20;
        public const int configArrayLength = 1;
        private bool m_LT64 = false;
        BitArray config = new BitArray(8* configArrayLength);

        //If variable length challenges are enabled, a 63 byte challenge is sent instead.
        //See GenerateChallenge() and http://forum.yubico.com/viewtopic.php?f=16&t=1078
        public bool LT64
        {
            get { return m_LT64; }
            set { m_LT64 = value; }
        }

        public YubiSlot YubikeySlot
        {
            get;
            set;
        }

        public bool RegenChallenge
        {
            get;
            set;
        }

        public KeeChallengeProv()
        {
            YubikeySlot = YubiSlot.AUTO;
        }

        private IOConnectionInfo mInfo;

        public override string Name
        {
            get {
                return m_name + "(" + YubikeySlot.ToString() + ")";
            }
        }

        public override bool SecureDesktopCompatible
        {
            get
            {
                return true;
            }
        }

        public override byte[] GetKey(KeyProviderQueryContext ctx)
        {
            if (ctx == null)
            {
                Debug.Assert(false);
                return null;
            }

            mInfo = ctx.DatabaseIOInfo.CloneDeep();
            string db = mInfo.Path;
            Regex rgx = new Regex(@"\.kdbx$");
            mInfo.Path = rgx.Replace(db, ".xml");

            if (Object.ReferenceEquals(db,mInfo.Path)) //no terminating .kdbx found-> maybe using keepass 1? should never happen...
            {
                MessageService.ShowWarning("Invalid database. KeeChallenge only works with .kdbx files.");
                return null;
            }


            try
            {
                if (ctx.CreatingNewKey) return Create(ctx);
                return Get(ctx);
            }
            catch (Exception ex) { MessageService.ShowWarning(ex.Message); }

            return null;
        }

        public byte[] GenerateChallenge()
        {
            byte[] chal =  CryptoRandom.Instance.GetRandomBytes(challengeLenBytes);  
            if (LT64)
            {
                chal[challengeLenBytes - 2] = (byte)~chal[challengeLenBytes - 1];
            }

            return chal;
        }

        public byte[] GenerateResponse(byte[] challenge, byte[] key)
        {
            HMACSHA1 hmac = new HMACSHA1(key);

            if (LT64)
                challenge = challenge.Take(challengeLenBytes - 1).ToArray();

            byte[] resp = hmac.ComputeHash(challenge);
            hmac.Clear();
            return resp;
        }

        private bool EncryptAndSave(byte[] secret, byte[] challenge, byte[] resp)
        {

            //use the response to encrypt the secret
            SHA256 sha = SHA256Managed.Create();
            byte[] key = sha.ComputeHash(resp); // get a 256 bit key from the 160 bit hmac response
            byte[] secretHash = sha.ComputeHash(secret);

            AesManaged aes = new AesManaged();
            aes.KeySize = key.Length * sizeof(byte) * 8; //pedantic, but foolproof
            aes.Key = key;
            aes.GenerateIV();
            aes.Padding = PaddingMode.PKCS7;
            byte[] iv = aes.IV;

            byte[] encrypted;

            //verification to ensure decryption worked
            byte[] verification = CryptoRandom.Instance.GetRandomBytes(10);
            ICryptoTransform enc = aes.CreateEncryptor();
            using (MemoryStream msEncrypt = new MemoryStream())
            {
                using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, enc, CryptoStreamMode.Write))
                {
                    //config values that should be stored encrypted to prevent tampering
                    byte[] ret = new byte[configArrayLength];
                    config.CopyTo(ret, 0);

                    csEncrypt.Write(secret, 0, secret.Length);
                    csEncrypt.Write(ret, 0, ret.Length);
                    csEncrypt.FlushFinalBlock();

                    encrypted = msEncrypt.ToArray();
                    csEncrypt.Close();
                    csEncrypt.Clear();
                }
                msEncrypt.Close();
            }

            sha.Clear();
            aes.Clear();

            Stream s = null;
            try
            {
                FileTransactionEx ft = new FileTransactionEx(mInfo,
                    false);
                s = ft.OpenWrite();
               
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.CloseOutput = true;
                settings.Indent = true;
                settings.IndentChars = "\t";
                settings.NewLineOnAttributes = true;

                XmlWriter xml = XmlWriter.Create(s, settings);
                xml.WriteStartDocument();
                xml.WriteStartElement("data");

                xml.WriteStartElement("aes");
                xml.WriteElementString("encrypted", Convert.ToBase64String(encrypted));
                xml.WriteElementString("iv", Convert.ToBase64String(iv));
                xml.WriteEndElement();

                xml.WriteElementString("challenge", Convert.ToBase64String(challenge));
                xml.WriteElementString("lt64", LT64.ToString());
                xml.WriteElementString("newType", true.ToString());

                xml.WriteEndElement();
                xml.WriteEndDocument();
                xml.Close();

                ft.CommitWrite();
            }
            catch (Exception)
            {
                MessageService.ShowWarning(String.Format("Error: unable to write to file {0}", mInfo.Path));
                return false;
            }    
            finally
            {                
                s.Close();
            }

            return true;
        }

        private static bool DecryptSecret(byte[] encryptedSecret, byte[] yubiResp, byte[] iv, byte[] verification,bool newType, out byte[] secret)
        {
            //use the response to decrypt the secret
            SHA256 sha = SHA256Managed.Create();
            byte[] key = sha.ComputeHash(yubiResp); // get a 256 bit key from the 160 bit hmac response

            AesManaged aes = new AesManaged();
            aes.KeySize = key.Length * sizeof(byte) * 8; //pedantic, but foolproof
            aes.Key = key;
            aes.IV = iv;
            aes.Padding = PaddingMode.PKCS7;

            if (newType)
            {
                secret = new byte[challengeLenBytes];
            }
            else
            {
                secret = new byte[keyLenBytes];
                
            }

            ICryptoTransform dec = aes.CreateDecryptor();
            using (MemoryStream msDecrypt = new MemoryStream(encryptedSecret))
            {
                using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, dec, CryptoStreamMode.Read))
                {
                    csDecrypt.Read(secret, 0, secret.Length);
                    csDecrypt.Close();
                    csDecrypt.Clear();
                }
                msDecrypt.Close();
            }

            //return the secret
            sha.Clear();
            aes.Clear();
            return true;
        }
        private bool ReadEncryptedSecretXML(out byte[] encryptedSecret, out byte[] challenge, out byte[] iv, out bool newType)
        {
            encryptedSecret = null;
            iv = null;
            challenge = null;
            newType = false;

            LT64 = false; //default to false if not found
            XmlReader xml = null;
            Stream s = null;
            try
            {
                s = IOConnection.OpenRead(mInfo);

                //read file

                XmlReaderSettings settings = new XmlReaderSettings();
                settings.CloseInput = true;
                xml = XmlReader.Create(s, settings);

                while (xml.Read())
                {
                    if (xml.IsStartElement())
                    {
                        switch (xml.Name)
                        {
                            case "encrypted":
                                xml.Read();
                                encryptedSecret = Convert.FromBase64String(xml.Value.Trim());
                                break;
                            case "iv":
                                xml.Read();
                                iv = Convert.FromBase64String(xml.Value.Trim());
                                break;
                            case "challenge":
                                xml.Read();
                                challenge = Convert.FromBase64String(xml.Value.Trim());
                                break;
                            case "lt64":
                                xml.Read();
                                if (!bool.TryParse(xml.Value.Trim(), out m_LT64)) throw new Exception("Unable to parse LT64 flag");
                                break;
                            case "newType":
                                xml.Read();
                                if (!bool.TryParse(xml.Value.Trim(), out newType)) throw new Exception("Unable to parse newType flag");
                                break;

                        }
                    }
                }
            }
            catch (Exception)
            {
                MessageService.ShowWarning(String.Format("Error: file {0} could not be read correctly. Is the file corrupt? Reverting to recovery mode", mInfo.Path));
                return false;
            }
            finally
            {
                if (xml != null)
                    xml.Close();
                if (s != null)
                    s.Close();
            }

            //if failed, return false
            return true;
        }


        private bool ReadEncryptedSecret(out byte[] encryptedSecret, out byte[] challenge, out byte[] iv, out bool newType)
        {
            encryptedSecret = null;
            iv = null;
            challenge = null;
            newType = false;
            
            LT64 = false; //default to false if not found
            return ReadEncryptedSecretXML(out encryptedSecret, out challenge, out iv, out newType);
        }

        private byte[] Create(KeyProviderQueryContext ctx)
        {

            byte[] challenge = GenerateChallenge();
            byte[] resp = new byte[YubiWrapper.yubiRespLen];
            KeyEntry entryForm = new KeyEntry(this, challenge);

            if (entryForm.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;

            byte[] secret = GenerateChallenge();

            //show the entry dialog for the secret
            //get the secret
            KeyCreation creator = new KeyCreation(this);

            entryForm.Response.CopyTo(resp, 0);
            Array.Clear(entryForm.Response, 0, entryForm.Response.Length);

            if (!EncryptAndSave(secret, challenge, resp))
            {
                return null;
            }

            //store the encrypted secret, the iv, and the challenge to disk           
            SHA256 sha = SHA256Managed.Create();
            byte[] hashedSecret = sha.ComputeHash(secret);
            return hashedSecret;
        }

        private byte[] Get(KeyProviderQueryContext ctx)
        {
            //read the challenge, iv, and encrypted secret from disk -- if missing, you must use recovery mode
            byte[] encryptedSecret = null;
            byte[] iv = null;
            byte[] challenge = null;
            byte[] verification = null;
            byte[] secret = null;
            byte[] configArray = null;
            bool newType = false;

            //show the dialog box prompting user to press yubikey button
            byte[] resp = new byte[YubiWrapper.yubiRespLen];
            KeyEntry entryForm = new KeyEntry(this, challenge);
            if (!ReadEncryptedSecret(out encryptedSecret, out challenge, out iv,out newType))
            {
                secret = RecoveryMode();
                EncryptAndSave(secret,challenge,resp);
                SHA256 sha = SHA256Managed.Create();
                byte[] hashedSecret = sha.ComputeHash(secret);
                return hashedSecret;
            }

            entryForm.Challenge = challenge;
            if (entryForm.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                if (entryForm.RecoveryMode)
                {
                    secret = RecoveryMode();
                    EncryptAndSave(secret, challenge, resp);
                    SHA256 sha = SHA256Managed.Create();
                    byte[] hashedSecret = sha.ComputeHash(secret);
                    return hashedSecret;
                }

                else return null;                
            }               

            entryForm.Response.CopyTo(resp,0);
            Array.Clear(entryForm.Response,0,entryForm.Response.Length);

            //attempt to decrypt the current secret
            if (DecryptSecret(encryptedSecret, resp, iv, verification,newType, out secret))
            {
                if (RegenChallenge)
                {
                    challenge = GenerateChallenge();
                    entryForm = new KeyEntry(this, challenge);
                    if (entryForm.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        entryForm.Response.CopyTo(resp, 0);
                        Array.Clear(entryForm.Response, 0, entryForm.Response.Length);
                        if (EncryptAndSave(secret, challenge, resp))
                        {
                            SHA256 sha = SHA256Managed.Create();
                            byte[] hashedSecret = sha.ComputeHash(secret);
                            return hashedSecret;
                        }
                        else return null;
                    }
                    else return null;
                }
                else
                {
                    SHA256 sha = SHA256Managed.Create();
                    byte[] hashedSecret = sha.ComputeHash(secret);
                    return hashedSecret;
                }
            }
            else
            {
                return null;
            }
        }

        private byte[] RecoveryMode()
        {
            //prompt user to enter secret
            RecoveryMode recovery = new RecoveryMode(this);
            if (recovery.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
            byte[] secret = new byte[recovery.Secret.Length];

            recovery.Secret.CopyTo(secret, 0);
            Array.Clear(recovery.Secret, 0, recovery.Secret.Length);            
             
            return secret;
       }

    }
}
