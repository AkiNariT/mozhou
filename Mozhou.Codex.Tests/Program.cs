using System.Text.Json;
using Mozhou.Codex;

if (args.Contains("app-server")) { await FakeServer(); return; }
if (args.Contains("--real-connect"))
{
    var path = CodexClient.FindExecutable() ?? throw new Exception("Codex executable not found");
    await using var real = new CodexClient(path, Path.Combine(Environment.CurrentDirectory,"work-protocol","real-client"));
    var status = await real.ConnectAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
    Console.WriteLine($"ChatGPT={status.IsChatGpt}; Plan={status.PlanType}; Models={real.Models.Count}");
    foreach(var m in real.Models) Console.WriteLine($"{m.Id} | {m.DisplayName} | default={m.IsDefault}");
    return;
}
var failed=0;
foreach(var scenario in new[]{"stream","apiKey","rpcError","earlyExit","cancel","failedTurn","approval","oversize","isolation","dispose","authSwitch","startHang"})
{
    var folder=Path.Combine(Environment.CurrentDirectory,"work-protocol","tests",Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder,"scenario"),scenario);
    try
    {
        await using var client=new CodexClient(Environment.ProcessPath!,folder);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        if(scenario=="apiKey")
        {
            var s=await client.ConnectAsync(timeout.Token); Check(!s.IsChatGpt,"API key must be rejected");
            await Throws(()=>client.GenerateAsync("test",null,null,timeout.Token),"ChatGPT");
        }
        else if(scenario=="earlyExit") await Throws(()=>client.ConnectAsync(timeout.Token),"退出");
        else if(scenario=="oversize") await Throws(()=>client.ConnectAsync(timeout.Token),"上限");
        else
        {
            var s=await client.ConnectAsync(timeout.Token); Check(s.IsChatGpt,"ChatGPT expected");
            Check(client.Models.Count==2,"model pagination expected");
            if(scenario=="cancel")
            {
                using var cancel=new CancellationTokenSource();
                var task=client.GenerateAsync("test","fixture-model",_=>cancel.Cancel(),cancel.Token);
                try { await task; throw new Exception("expected cancellation"); } catch(OperationCanceledException) {}
                Check(File.Exists(Path.Combine(folder,"interrupted")),"turn/interrupt must reach child");
            }
            else if(scenario=="dispose")
            {
                var task=client.GenerateAsync("test",null,null,timeout.Token);
                await Task.Delay(200); await client.DisposeAsync();
                await Throws(()=>task,"关闭");
            }
            else if(scenario=="rpcError") await Throws(()=>client.GenerateAsync("test",null,null,timeout.Token),"fixture rpc error");
            else if(scenario=="failedTurn") await Throws(()=>client.GenerateAsync("test",null,null,timeout.Token),"fixture quota failure");
            else if(scenario=="authSwitch") await Throws(()=>client.GenerateAsync("test",null,null,timeout.Token),"ChatGPT");
            else if(scenario=="startHang")
            {
                using var cancel=new CancellationTokenSource(300);
                try { await client.GenerateAsync("test",null,null,cancel.Token); throw new Exception("expected cancellation"); } catch(OperationCanceledException) {}
                await Throws(()=>client.GenerateAsync("again",null,null,timeout.Token),"退出");
            }
            else
            {
                var deltas=new List<string>(); var answer=await client.GenerateAsync("test","fixture-model",deltas.Add,timeout.Token);
                Check(answer=="墨舟","stream result mismatch"); Check(string.Concat(deltas)=="墨舟","delta callback mismatch");
                if(scenario=="approval") Check(File.Exists(Path.Combine(folder,"denied")),"approval was not rejected");
            }
        }
        Console.WriteLine($"PASS {scenario}");
    }
    catch(Exception ex) { failed++; Console.WriteLine($"FAIL {scenario}: {ex.GetType().Name}: {ex.Message}"); }
}
Environment.ExitCode=failed==0?0:1;
static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
static async Task Throws(Func<Task> action,string contains)
{
    try { await action(); } catch(Exception ex) when(ex.Message.Contains(contains,StringComparison.OrdinalIgnoreCase)) { return; }
    throw new Exception("Expected exception containing: "+contains);
}
static async Task FakeServer()
{
    var scenario=File.ReadAllText("scenario");
    Console.InputEncoding=System.Text.Encoding.UTF8; Console.OutputEncoding=new System.Text.UTF8Encoding(false);
    if(scenario=="earlyExit") { Console.Error.WriteLine("fixture exit"); return; }
    if(scenario=="oversize") { Console.WriteLine(new string('x',2_200_000)); return; }
    var thread="fixture-thread"; var turn="fixture-turn";
    var accountReads=0;
    async Task Send(object x) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(x)); await Console.Out.FlushAsync(); }
    async Task Note(string method,object p) => await Send(new {method,@params=p});
    async Task Complete(string status="completed",string? error=null) => await Note("turn/completed",new {threadId=thread,turn=new {id=turn,status,items=Array.Empty<object>(),error=error==null?null:new {message=error}}});
    while(await Console.In.ReadLineAsync() is {} line)
    {
        var m=JsonDocument.Parse(line).RootElement;
        if(!m.TryGetProperty("method",out var method))
        {
            if(m.TryGetProperty("id",out var rid) && rid.ToString()=="approval-1")
            {
                Check(m.GetProperty("result").GetProperty("decision").GetString()=="decline","must decline approval");
                File.WriteAllText("denied","ok");
                await Note("item/agentMessage/delta",new {threadId=thread,turnId=turn,itemId="i",delta="墨舟"}); await Complete();
            }
            continue;
        }
        var name=method.GetString(); var id=m.TryGetProperty("id",out var value)?value.Clone():default;
        var p=m.TryGetProperty("params",out var pr)?pr:default;
        switch(name)
        {
            case "initialize":
                if(scenario=="isolation")
                {
                    foreach(System.Collections.DictionaryEntry item in Environment.GetEnvironmentVariables())
                        Check(!item.Key.ToString()!.StartsWith("CODEX_"),"parent CODEX environment leak");
                }
                await Send(new {id,result=new {userAgent="fixture"}}); break;
            case "account/read":
                accountReads++;
                await Send(new {id,result=new {account=new {type=scenario=="apiKey"||(scenario=="authSwitch"&&accountReads>1)?"apiKey":"chatgpt",planType="plus"}}}); break;
            case "model/list":
                var second=p.TryGetProperty("cursor",out var cursor)&&cursor.ValueKind==JsonValueKind.String;
                await Send(new {id,result=new {data=new[]{new {id=second?"second":"fixture-id",model=second?"second":"fixture-model",displayName="Fixture",isDefault=!second}},nextCursor=second?null:"page2"}}); break;
            case "thread/start":
                Check(p.GetProperty("sandbox").GetString()=="read-only","unsafe thread sandbox");
                Check(p.GetProperty("approvalPolicy").GetString()=="never","unsafe thread approval");
                Check(p.GetProperty("ephemeral").GetBoolean(),"must use ephemeral thread");
                Check(p.GetProperty("config").GetProperty("features.shell_tool").GetBoolean()==false,"shell must be disabled");
                await Send(new {id,result=new {thread=new {id=thread}}}); break;
            case "turn/start":
                Check(p.GetProperty("sandboxPolicy").GetProperty("type").GetString()=="readOnly","unsafe turn sandbox");
                if(scenario=="startHang") break;
                if(scenario=="rpcError") { await Send(new {id,error=new {code=-1,message="fixture rpc error"}}); break; }
                await Send(new {id,result=new {turn=new {id=turn,status="inProgress"}}});
                if(scenario=="dispose") break;
                if(scenario=="failedTurn") { await Complete("failed","fixture quota failure"); break; }
                if(scenario=="approval") { await Send(new {id="approval-1",method="item/commandExecution/requestApproval",@params=new {threadId=thread,turnId=turn}}); break; }
                await Note("item/agentMessage/delta",new {threadId="unrelated",turnId=turn,itemId="i",delta="wrong"});
                await Note("item/agentMessage/delta",new {threadId=thread,turnId=turn,itemId="i",delta="墨"});
                if(scenario=="cancel") break;
                await Note("item/agentMessage/delta",new {threadId=thread,turnId=turn,itemId="i",delta="舟"}); await Complete(); break;
            case "turn/interrupt": File.WriteAllText("interrupted","ok"); await Send(new {id,result=new {}}); await Complete("interrupted"); break;
            case "thread/unsubscribe": await Send(new {id,result=new {}}); break;
        }
    }
}
