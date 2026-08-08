namespace LabelFrame.Server;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        // 迭代 0 占位：健康检查。设备注册 / 作业投递在迭代 3 实现。
        app.MapGet("/health", () => Results.Ok(new
        {
            service = "LabelFrame.Server",
            status = "ok",
        }));

        app.Run();
    }
}