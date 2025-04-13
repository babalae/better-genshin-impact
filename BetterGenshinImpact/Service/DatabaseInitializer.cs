using System;
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
            _logger.LogInformation("正在初始化数据库...");
            
            // 确保数据库已创建
            _context.Database.EnsureCreated();
            
            // 应用所有迁移
            _context.Database.Migrate();
            
            _logger.LogInformation("数据库初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "数据库初始化失败");
            throw;
        }
    }
}