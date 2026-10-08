using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// コピー機マスタの結合テスト。外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// </summary>
public class CopierMachineServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string ReferenceCustomer = "CUS001";

    [Fact]
    public Task 登録_更新_無効化ができる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CopierMachineService>();

        var created = await service.CreateAsync(NewMachine("__TCM01", ReferenceCustomer, "初期機種", null));
        Assert.Equal("初期機種", (await service.GetByMachineNoAsync("__TCM01"))!.MachineModel);

        var updated = await service.UpdateAsync(NewMachine("__TCM01", ReferenceCustomer, "変更後", created.RowVersion));
        Assert.Equal("変更後", updated.MachineModel);

        var current = (await service.GetByMachineNoAsync("__TCM01"))!;
        await service.DeactivateAsync(NewMachine("__TCM01", ReferenceCustomer, "変更後", current.RowVersion));

        Assert.True((await service.GetByMachineNoAsync("__TCM01"))!.IsDeleted);
        Assert.DoesNotContain(await service.GetCopierMachinesAsync(), m => m.MachineNo == "__TCM01");
    });

    [Fact]
    public Task 存在しない得意先は拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CopierMachineService>();

        await Assert.ThrowsAsync<CopierMachineValidationException>(
            () => service.CreateAsync(NewMachine("__TCM02", "__NOCUS", null, null)));
        Assert.Null(await service.GetByMachineNoAsync("__TCM02"));
    });

    [Fact]
    public Task 無効な得意先は新規登録と変更で拒否される() => RunAsync(async sp =>
    {
        var customerService = sp.GetRequiredService<CustomerService>();
        var customer = await customerService.CreateAsync(NewCustomer("__TCM90"));
        await customerService.DeactivateAsync(customer);

        var service = sp.GetRequiredService<CopierMachineService>();
        await Assert.ThrowsAsync<CopierMachineValidationException>(
            () => service.CreateAsync(NewMachine("__TCM03", "__TCM90", null, null)));

        var created = await service.CreateAsync(NewMachine("__TCM04", ReferenceCustomer, null, null));
        await Assert.ThrowsAsync<CopierMachineValidationException>(
            () => service.UpdateAsync(NewMachine("__TCM04", "__TCM90", null, created.RowVersion)));
    });

    [Fact]
    public Task 重複した機番は拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CopierMachineService>();
        await service.CreateAsync(NewMachine("__TCM05", ReferenceCustomer, null, null));

        await Assert.ThrowsAsync<CopierMachineValidationException>(
            () => service.CreateAsync(NewMachine("__TCM05", ReferenceCustomer, null, null)));
    });

    [Fact]
    public Task 古いRowVersionでの更新と無効化は競合になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CopierMachineService>();
        var created = await service.CreateAsync(NewMachine("__TCM06", ReferenceCustomer, "初期", null));
        var stale = (byte[])created.RowVersion!.Clone();

        await service.UpdateAsync(NewMachine("__TCM06", ReferenceCustomer, "先勝ち", stale));

        await Assert.ThrowsAsync<CopierMachineConcurrencyException>(
            () => service.UpdateAsync(NewMachine("__TCM06", ReferenceCustomer, "後負け", stale)));
        await Assert.ThrowsAsync<CopierMachineConcurrencyException>(
            () => service.DeactivateAsync(NewMachine("__TCM06", ReferenceCustomer, "後負け", stale)));
        Assert.Equal("先勝ち", (await service.GetByMachineNoAsync("__TCM06"))!.MachineModel);
    });

    [Fact]
    public Task 履歴は主キーとFKで守られる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CopierMachineService>();
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        await service.CreateAsync(NewMachine("__TCM07", ReferenceCustomer, null, null));

        var closingDate = new DateOnly(2026, 9, 20);
        dbContext.CopierImportHistories.Add(NewHistory("__TCM07", closingDate));
        await dbContext.SaveChangesAsync();

        // 同一機番・締日の2件目は主キー違反。
        dbContext.ChangeTracker.Clear();
        dbContext.CopierImportHistories.Add(NewHistory("__TCM07", closingDate));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        dbContext.ChangeTracker.Clear();

        // 未登録機番の履歴はFK違反。
        dbContext.CopierImportHistories.Add(NewHistory("__NOMACHINE", closingDate));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    });

    private async Task RunAsync(Func<IServiceProvider, Task> body)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await body(scope.ServiceProvider);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static CopierMachine NewMachine(string machineNo, string customerCode, string? model, byte[]? rowVersion) => new()
    {
        MachineNo = machineNo,
        CustomerCode = customerCode,
        MachineModel = model,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        RowVersion = rowVersion,
    };

    private static CopierImportHistory NewHistory(string machineNo, DateOnly closingDate) => new()
    {
        MachineNo = machineNo,
        ClosingDate = closingDate,
        SalesSlipNumber = "__TEST",
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
    };

    private static Customer NewCustomer(string code) => new()
    {
        CustomerCode = code,
        CustomerName = "テスト得意先",
        ClosingDay = 15,
        TaxUnit = TaxUnit.Invoice,
        RoundingType = RoundingType.Floor,
        PrintRepresentativeFlag = false,
        BillingCustomerCode = code,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
    };
}
