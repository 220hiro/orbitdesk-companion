using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

partial class Companion {
    // Double-click distribution: per-user install, no PowerShell or administrator prompt.
    // Never touches legacy TerminalDesk directories, services or pairing credentials.
    static string[] FirstRun(){
        try{
            string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string directory=Path.Combine(local,"Programs","TerminalDeskNext");
            string profile=Path.Combine(local,"TerminalDeskNext");
            string executable=Path.Combine(directory,"TerminalDeskNext.exe");
            string current=Process.GetCurrentProcess().MainModule.FileName;
            string shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"TerminalDesk Next.lnk");
            bool installed=File.Exists(shortcut)&&File.Exists(executable);
            if(!installed && MessageBox.Show("Включить дополнительные функции обоев?\n\nПомощник будет запускаться вместе с Windows без значка в трее. Для первого подключения нажмите кнопку в обоях и подтвердите совпадающий код.\n\nАвтозапуск можно отключить в диспетчере задач Windows.","TerminalDesk Next",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return null;
            Directory.CreateDirectory(directory);Directory.CreateDirectory(profile);
            if(!string.Equals(Path.GetFullPath(current),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase)){
                // Refuse to overwrite a running installation; starting it is harmless (singleton).
                if(!File.Exists(executable))File.Copy(current,executable);
            }
            string arguments="--data \""+profile+"\" --port 18765";
            object shell=null,link=null;
            try{
                shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{shortcut});
                link.GetType().InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{executable});
                link.GetType().InvokeMember("Arguments",BindingFlags.SetProperty,null,link,new object[]{arguments});
                link.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,link,new object[0]);
            }finally{if(link!=null&&Marshal.IsComObject(link))Marshal.ReleaseComObject(link);if(shell!=null&&Marshal.IsComObject(shell))Marshal.ReleaseComObject(shell);}
            if(!string.Equals(Path.GetFullPath(current),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase)){
                Process.Start(new ProcessStartInfo(executable,arguments){UseShellExecute=false,CreateNoWindow=true});return null;
            }
            return new[]{"--data",profile,"--port","18765"};
        }catch(Exception error){MessageBox.Show("Не удалось запустить помощник: "+error.Message,"TerminalDesk Next",MessageBoxButtons.OK,MessageBoxIcon.Error);return null;}
    }
}
