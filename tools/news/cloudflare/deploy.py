"""Заливает воркер-будильник news-cron на Cloudflare и ставит ему cron раз в 30 минут.

    python deploy.py

Секреты в вывод не печатаются:
  CLOUDFLARE_API_TOKEN, CLOUDFLARE_ACCOUNT_ID — из переменных окружения или файла --secrets (строки KEY=value);
  токен GitHub — из ~/.config/echo-angmara/gh_dispatch_token.
"""
import argparse
import json
import os
import re
import sys

import requests

NAME = "echo-angmara-news-cron"
CRON = "7,37 * * * *"  # как в news.yml: не ровно :00/:30
HERE = os.path.dirname(os.path.abspath(__file__))


def secrets(path):
    d = {k: os.environ[k] for k in ("CLOUDFLARE_API_TOKEN", "CLOUDFLARE_ACCOUNT_ID") if k in os.environ}
    if len(d) < 2 and os.path.exists(path):
        for line in open(path, encoding="utf-8"):
            m = re.match(r"^([A-Z_]+)=(.*)$", line.strip())
            if m:
                d.setdefault(m[1], m[2].strip())
    return d


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--secrets", default=os.path.expanduser("~/.config/barber/secrets.txt"))
    args = ap.parse_args()
    s = secrets(args.secrets)
    gh = open(os.path.expanduser("~/.config/echo-angmara/gh_dispatch_token"), encoding="utf-8").read().strip()

    h = {"Authorization": "Bearer " + s["CLOUDFLARE_API_TOKEN"]}
    a = "https://api.cloudflare.com/client/v4/accounts/" + s["CLOUDFLARE_ACCOUNT_ID"]
    meta = {
        "main_module": "worker.js",
        "compatibility_date": "2025-09-01",
        "bindings": [{"type": "secret_text", "name": "GH_TOKEN", "text": gh}],
        "observability": {"enabled": True},
    }
    files = [
        ("metadata", (None, json.dumps(meta), "application/json")),
        ("worker.js", ("worker.js", open(os.path.join(HERE, "worker.js"), "rb").read(), "application/javascript+module")),
    ]
    r = requests.put(f"{a}/workers/scripts/{NAME}", headers=h, files=files, timeout=60).json()
    print("upload", r.get("success"), r.get("errors"))
    if not r.get("success"):
        sys.exit(1)
    r = requests.put(f"{a}/workers/scripts/{NAME}/schedules", headers=h, json=[{"cron": CRON}], timeout=30).json()
    print("cron", r.get("success"), r.get("errors"), CRON)


if __name__ == "__main__":
    main()
