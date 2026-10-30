#!/usr/bin/env python3
"""Asks the Jev model (through jev-ai.pro) multiple-choice and yes/no questions, and returns its answers.

jev-ai.pro is a third party that names no company and forwards requests to unnamed providers, so only short job
descriptions go through here: never chat messages, code, or anything private to the project.
"""
import json
import os
import urllib.error
import urllib.request

URL = "https://jev-ai.pro/api/v1/systemone"
MODEL = "jev-latest"
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def api_key():
    key = os.environ.get("JEV_AI_API_KEY")
    if key:
        return key
    try:
        with open(os.path.join(REPO, "settings.env"), encoding="utf-8") as f:
            for line in f:
                if line.startswith("JEV_AI_API_KEY="):
                    return line.split("=", 1)[1].strip()
    except OSError:
        pass
    return None


def ask(state, questions, timeout=15):
    """state: dict of facts. questions: {name: {"type": "choice", "instructions": ..., "criteria": {option: meaning}}}
    or {"type": "noul", "instructions": ...} for yes/no. Returns the whole reply; raises on any failure."""
    key = api_key()
    if not key:
        raise RuntimeError("no JEV_AI_API_KEY in the environment or settings.env")
    body = json.dumps({"model": MODEL, "state": state, "questions": questions}).encode("utf-8")
    req = urllib.request.Request(URL, data=body, method="POST", headers={
        "Authorization": "Bearer " + key, "Content-Type": "application/json", "Accept": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        raise RuntimeError("Jev answered %d: %s" % (e.code, e.read()[:300].decode("utf-8", "replace")))
