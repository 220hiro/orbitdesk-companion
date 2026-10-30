using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

partial class Companion {
    // No persistent icon. A dialog is shown only for a newly initiated pairing request.
    static void StartPairingPrompts(){
        var thread=new Thread(()=>{
            using(var timer=new System.Windows.Forms.Timer()){
                string shown=null;DateTime lastPrompt=DateTime.MinValue;
                timer.Interval=500;
                timer.Tick+=(s,e)=>{
                    if(shutdown.IsCancellationRequested){Application.ExitThread();return;}
                    string code=PendingPairCode();
                    if(code==null||code==shown||(DateTime.UtcNow-lastPrompt).TotalSeconds<60)return;
                    shown=code;lastPrompt=DateTime.UtcNow;
                    bool accept=MessageBox.Show("Код в обоях: "+code+"\n\nЕсли вы только что нажали «Подключить Companion» и код совпадает, разрешите доступ к показателям компьютера и управлению музыкой.\n\nЕсли запрос не ваш — нажмите «Нет».","Подключить TerminalDesk Next?",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
                    DecidePair(code,accept);
                };
                timer.Start();Application.Run();
            }
        });thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
    }
    static void StartTray(){
        var thread=new Thread(()=>{
            using(var icon=new NotifyIcon())using(var menu=new ContextMenuStrip())using(var timer=new System.Windows.Forms.Timer()){
                menu.Items.Add("Подключить обои…",null,(s,e)=>{
                    string code=PendingPairCode();
                    if(code==null){MessageBox.Show("Нажмите «Подключить Companion» в обоях, затем откройте этот пункт снова.","TerminalDesk Next");return;}
                    bool accept=MessageBox.Show("Сравните код с карточкой в обоях: "+code+"\n\nРазрешить доступ к статистике приложений, заряду устройств и управлению музыкой?\nРазрешайте только запрос, который вы начали сами.","Подключение TerminalDesk Next",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;
                    DecidePair(code,accept);
                });
                menu.Items.Add("Отключить доступ всех обоев",null,(s,e)=>RevokePairs());
                menu.Items.Add("Copy pairing key",null,(s,e)=>{try{Clipboard.SetText(key);}catch{}});
                menu.Items.Add("Open settings folder",null,(s,e)=>{try{Process.Start("explorer.exe",dataDir);}catch{}});
                menu.Items.Add("Exit TerminalDesk Next",null,(s,e)=>{shutdown.Cancel();Application.ExitThread();});
                icon.Icon=SystemIcons.Application;icon.Text="TerminalDesk Next Companion";icon.ContextMenuStrip=menu;icon.Visible=true;
                string shown=null;timer.Interval=1000;timer.Tick+=(s,e)=>{string pending=PendingPairCode();if(pending!=null&&pending!=shown){shown=pending;icon.ShowBalloonTip(5000,"Подключение обоев","Откройте меню значка → Подключить обои и сравните код.",ToolTipIcon.Info);}if(shutdown.IsCancellationRequested)Application.ExitThread();};timer.Start();
                Application.Run();icon.Visible=false;
            }
        });thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
    }
}
