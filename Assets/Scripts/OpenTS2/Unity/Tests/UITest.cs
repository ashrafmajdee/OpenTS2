﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using OpenTS2.Content;
using OpenTS2.Common;
using OpenTS2.Unity.Content;

namespace OpenTS2.Unity.Tests
{
    public class UITest : MonoBehaviour
    {
        public RawImage image;
        public string packageToLoad = "E:/EA Games/bb/Downloads/Mods/zld_Nannies/ld_nanny_phone.package";

        void Start()
        {
            ContentManager.Provider.AddPackage(packageToLoad);
            var texture = ContentManager.Provider.GetAsset<TextureAsset>(new TGI(0x00000001, 0x7FFFD3D2, 0x856DDBAC));
            image.texture = texture.engineTexture as Texture2D;
        }
    }
}