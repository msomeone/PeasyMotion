using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using System.ComponentModel.Design;
using EnvDTE80;

namespace PeasyMotion
{
    public class CommandExecutorService
    {
        readonly DTE _dte;

        public CommandExecutorService()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _dte = Package.GetGlobalService(typeof(SDTE)) as DTE;
        }

        public bool IsCommandAvailable(string commandName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return FindCommand(_dte.Commands, commandName) != null;
        }

        public void Execute(string commandName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _dte.ExecuteCommand(commandName);
        }

        private static dynamic FindCommand(Commands commands, string commandName)
        {
            foreach (var command in commands)
            {
                if (((dynamic)command).Name == commandName)
                {
                    return command;
                }
            }
            return null;
        }
    }
}
