using System;
using System.Diagnostics;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Model.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

public class DatabaseInitializer
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(ApplicationDbContext context, ILogger<DatabaseInitializer> logger)
    {
        _context = context;
        _logger = logger;
    }

    public void Initialize()
    {
        try
        {
            Debug.WriteLine("正在初始化数据库...");
            
            // 应用所有迁移
            _context.Database.Migrate();
            
            Debug.WriteLine("数据库初始化完成");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Debug.WriteLine(ex.StackTrace);
            _logger.LogDebug(ex, "数据库初始化失败");
            throw;
        }
    }
}