# HTML -> PDF (Playwright + 手元の Chrome) -> PNG（目視確認用）
# 使い方: python render.py   （このフォルダで実行）
import pathlib, sys
from playwright.sync_api import sync_playwright
import pypdfium2 as pdfium

here = pathlib.Path(__file__).parent
names = sys.argv[1:] or ["pattern-a-serious", "pattern-b-fresh", "pattern-c-pop"]
(here / "preview").mkdir(exist_ok=True)

with sync_playwright() as p:
    browser = p.chromium.launch(channel="chrome")
    page = browser.new_page()
    for n in names:
        page.goto((here / f"{n}.html").as_uri(), wait_until="networkidle")
        page.evaluate("document.fonts.ready")
        # 文字を直接持つ要素が、ページ端から10mm の安全域に入っていないか（.deco は装飾なので除外）
        # 併せて 9pt 未満の文字も報告する
        over = page.evaluate("""() => {
          const mm = 96 / 25.4, out = [];
          for (const e of document.querySelectorAll('.page *')) {
            if (e.closest('.deco')) continue;
            const hasText = [...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim());
            if (!hasText) continue;
            const p = e.closest('.page').getBoundingClientRect(), r = e.getBoundingClientRect();
            const t = e.textContent.trim().slice(0, 16);
            if (r.left < p.left + 10 * mm || r.right > p.right - 10 * mm || r.top < p.top + 10 * mm || r.bottom > p.bottom - 10 * mm)
              out.push('edge: ' + t);
            if (parseFloat(getComputedStyle(e).fontSize) < 12) out.push('small: ' + t);
          }
          return out; }""")
        page.pdf(path=str(here / f"{n}.pdf"), prefer_css_page_size=True, print_background=True)
        pdf = pdfium.PdfDocument(str(here / f"{n}.pdf"))
        print(n, "pages:", len(pdf), "overflow:", over)
        for i in range(len(pdf)):
            pdf[i].render(scale=1.4).to_pil().save(here / "preview" / f"{n}-p{i + 1}.png")
    browser.close()
