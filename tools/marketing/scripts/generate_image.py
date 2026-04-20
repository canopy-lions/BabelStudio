"""Generate marketing images via Gemini image generation API."""

import argparse
import base64
import os
import sys
from pathlib import Path

import google.generativeai as genai
from PIL import Image
import io


MODELS = {
    "flash": "gemini-2.0-flash-preview-image-generation",
    "pro": "gemini-2.5-pro-preview-06-05",
}

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
    response = model.generate_content(
        full_prompt,
        generation_config={"response_modalities": ["image"]},
    )

    for part in response.parts:
        if hasattr(part, "inline_data"):
            image_data = base64.b64decode(part.inline_data.data)
            img = Image.open(io.BytesIO(image_data))
            output.parent.mkdir(parents=True, exist_ok=True)
            img.save(str(output))
            print(f"Saved: {output}")
            return

    print("No image in response", file=sys.stderr)
    sys.exit(1)


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate marketing images via Gemini")
    parser.add_argument("prompt", help="Image generation prompt")
    parser.add_argument("--output", "-o", type=Path, required=True)
    parser.add_argument("--model", choices=["flash", "pro"], default="flash")
    parser.add_argument("--aspect", default="16:9", choices=list(ASPECT_RATIOS))
    args = parser.parse_args()
    generate(args.prompt, args.output, args.model, args.aspect)


if __name__ == "__main__":
    main()
