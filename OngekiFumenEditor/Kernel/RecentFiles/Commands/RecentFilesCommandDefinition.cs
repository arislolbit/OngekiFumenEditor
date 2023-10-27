﻿using Gemini.Framework.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OngekiFumenEditor.Kernel.RecentFiles.Commands
{
    [CommandDefinition]
    public class RecentFilesCommandDefinition : CommandDefinition
    {
        public const string CommandName = "File.RecentFiles";

        public override string Name
        {
            get { return CommandName; }
        }

        public override string Text
        {
            get { return "最近打开的文件..."; }
        }

        public override string ToolTip => string.Empty;
    }
}
