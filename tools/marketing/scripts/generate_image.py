"""Generate marketing images via Gemini image generation API."""

import argparse
import base64
from binascii import Error as BinasciiError
import io
import logging
import os
import sys
from pathlib import Path

from google.api_core.exceptions import GoogleAPIError
import google.generativeai as genai
from PIL import Image, UnidentifiedImageError


MODELS = {
    "flash": "gemini-2.0-flash-preview-image-generation",
    "pro": "gemini-2.5-pro-preview-06-05",
}

logger = logging.getLogger(__name__)

ASPECT_RATIOS = {
    "1:1": (1024, 1024),
    "16:9": (1920, 1080),
    "9:16": (1080, 1920),
    "3:1": (1500, 500),
    "4:1": (1584, 396),
    "3:2": (1200, 800),
}


def generate(prompt: str, output: Path, model_key: str, aspect: str) -> None:
    api_key = os.environ.get("GEMINI_API_KEY")
    if not api_key:
        print("Error: GEMINI_API_KEY environment variable not set", file=sys.stderr)
        sys.exit(1)

    genai.configure(api_key=api_key)
    model = genai.GenerativeModel(MODELS[model_key])

    width, height = ASPECT_RATIOS.get(aspect, (1024, 1024))
    full_prompt = f"{prompt}. No text, no letters, no words. {width}x{height} composition."

    print(f"Generating with {model_key} ({MODELS[model_key]})...")
    try:
        response = model.generate_content(
            full_prompt,
            generation_config={"response_modalities": ["image"]},
        )
    except GoogleAPIError as ex:  # pragma: no cover - network/API failure path
        logger.error("Failed to generate image from Gemini: %s", ex)
        raise SystemExit(1) from ex
    except Exception as ex:  # pragma: no cover - SDKs can surface transport errors outside GoogleAPIError
        logger.error("Unexpected Gemini SDK failure: %s", ex)
        raise SystemExit(1) from ex

    for part in response.parts:
        if hasattr(part, "inline_data"):
            try:
                image_data = base64.b64decode(part.inline_data.data)
                with Image.open(io.BytesIO(image_data)) as img:
                    output.parent.mkdir(parents=True, exist_ok=True)
                    img.save(str(output))
                print(f"Saved: {output}")
                return
            except (BinasciiError, OSError, UnidentifiedImageError) as ex:
                logger.error("Failed to decode or save generated image: %s", ex)
                raise SystemExit(1) from ex

    print("No image in response", file=sys.stderr)
    sys.exit(1)


def main() -> None:
    logging.basicConfig(level=logging.WARNING, format="%(levelname)s: %(message)s")
    parser = argparse.ArgumentParser(description="Generate marketing images via Gemini")
    parser.add_argument("prompt", help="Image generation prompt")
    parser.add_argument("--output", "-o", type=Path, required=True)
    parser.add_argument("--model", choices=["flash", "pro"], default="flash")
    parser.add_argument("--aspect", default="16:9", choices=list(ASPECT_RATIOS))
    args = parser.parse_args()
    generate(args.prompt, args.output, args.model, args.aspect)


if __name__ == "__main__":
    main()
