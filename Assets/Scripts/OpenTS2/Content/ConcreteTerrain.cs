﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTS2.Common;
using OpenTS2.Files.Formats.DBPF;

namespace OpenTS2.Content
{
    // Simplest terrain so far.
    public class ConcreteTerrain : TerrainType
    {
        public override string Name => "Concrete";

        public override bool MakeVariation => false;

        public override ResourceKey Texture => new ResourceKey("concrete-smooth_txtr", GroupIDs.Scenegraph, TypeIDs.SCENEGRAPH_TXTR);

        public override ResourceKey Roughness => new ResourceKey("dirt-grey_txtr", GroupIDs.Scenegraph, TypeIDs.SCENEGRAPH_TXTR);
    }
}
