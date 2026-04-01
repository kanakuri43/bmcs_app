using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

/// <summary>
/// コード欄 Space キーによるマスタ検索ダイアログのサービス
/// </summary>
public interface ILookupService
{
    Customer? OpenCustomerSearch(string initialKeyword = "");
    Employee? OpenEmployeeSearch(string initialKeyword = "");
    Product?  OpenProductSearch(string initialKeyword = "");

    /// <summary>伝票番号検索ダイアログを開き、選択された伝票番号を返す</summary>
    string? OpenSlipSearch(IEnumerable<SlipSummary> slips, string initialKeyword = "");

    /// <summary>コードで得意先を直接検索（Enter/Tab 補完用）</summary>
    Customer? FindCustomerByCode(string code);
    /// <summary>コードで担当者を直接検索</summary>
    Employee? FindEmployeeByCode(string code);
    /// <summary>コードで商品を直接検索</summary>
    Product?  FindProductByCode(string code);
}
