"""Render an HTML file to PNG at exact pixel dimensions using Playwright."""

import argparse
import asyncio
from pathlib import Path

from playwright.async_api import async_playwright


def positive_int(value: str) -> int:
    try:
        parsed = int(value)
    except ValueError as ex:
        raise argparse.ArgumentTypeError(f"'{value}' is not a valid integer") from ex

    if parsed <= 0:
        raise argparse.ArgumentTypeError("value must be a positive integer")

    return parsed


async def screenshot(html_path: Path, output: Path, width: int, height: int) -> None:
    async with async_playwright() as p:
        browser = await p.chromium.launch(channel="chrome")
        try:
            page = await browser.new_page(viewport={"width": width, "height": height})
            await page.goto(html_path.resolve().as_uri())
            await page.wait_for_load_state("networkidle")
            output.parent.mkdir(parents=True, exist_ok=True)
            await page.screenshot(path=str(output), clip={"x": 0, "y": 0, "width": width, "height": height})
        finally:
            await browser.close()
    print(f"Saved: {output} ({width}x{height})")


def main() -> None:
    parser = argparse.ArgumentParser(description="Screenshot an HTML banner at exact dimensions")
    parser.add_argument("html", type=Path, help="Path to HTML file")
    parser.add_argument("--output", "-o", type=Path, required=True, help="Output PNG path")
    parser.add_argument("--width", "-W", type=positive_int, required=True)
    parser.add_argument("--height", "-H", type=positive_int, required=True)
    args = parser.parse_args()
    asyncio.run(screenshot(args.html, args.output, args.width, args.height))


if __name__ == "__main__":
    main()
