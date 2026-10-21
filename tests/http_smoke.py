#!/usr/bin/env python3
"""HTTP checks against a running Local preview; standard library only.

Creates one uniquely named Listener and a comment in local test data. This script
does not configure AWS or clean/delete the database. Use a disposable local data
folder when you want a pristine preview afterwards.
"""

import http.cookiejar
import json
import re
import sys
import uuid
from html import unescape
from html.parser import HTMLParser
from urllib.error import HTTPError
from urllib.parse import urlencode, urljoin, urlparse
from urllib.request import HTTPCookieProcessor, HTTPRedirectHandler, Request, build_opener


class Page(HTMLParser):
    def __init__(self, source):
        super().__init__()
        self.inputs = {}
        self.links = []
        self.text = []
        self.feed(source)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "input" and attrs.get("name"):
            self.inputs[attrs["name"]] = attrs.get("value", "")
        if tag == "meta" and attrs.get("name", "").lower() in {
            "request-verification-token", "csrf-token", "__requestverificationtoken"
        }:
            self.inputs["__RequestVerificationToken"] = attrs.get("content", "")
        if tag == "a" and attrs.get("href"):
            self.links.append(unescape(attrs["href"]))

    def handle_data(self, data):
        self.text.append(data)

    def token(self):
        value = self.inputs.get("__RequestVerificationToken")
        if not value:
            raise AssertionError("No anti-forgery token found on the page")
        return value


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Client:
    def __init__(self, base):
        self.base = base.rstrip("/") + "/"
        self.cookies = http.cookiejar.CookieJar()
        self.opener = build_opener(HTTPCookieProcessor(self.cookies))
        self.direct = build_opener(HTTPCookieProcessor(self.cookies), NoRedirect())

    def request(self, path, fields=None, redirect=True):
        request = Request(
            urljoin(self.base, path.lstrip("/")),
            data=None if fields is None else urlencode(fields).encode(),
            headers={"User-Agent": "MickMouse-Local-Smoke/1.0"},
        )
        try:
            with (self.opener if redirect else self.direct).open(request, timeout=30) as response:
                return response.status, response.read().decode(), response.geturl()
        except HTTPError as error:
            return error.code, error.read().decode(), error.geturl()

    def get(self, path):
        status, body, _ = self.request(path)
        assert status == 200, f"GET {path}: expected 200, received {status}"
        return body

    def post(self, path, token, **fields):
        return self.request(path, {"__RequestVerificationToken": token, **fields})


def check(condition, message):
    assert condition, message
    print("PASS " + message)


def main():
    base = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5000"
    check(urlparse(base).hostname in {"localhost", "127.0.0.1", "::1"},
          "Target is a loopback Local preview")
    client = Client(base)
    home = client.get("/Episodes")
    visible = " ".join(Page(home).text)
    check(bool(re.search(r"local\s+(preview|mode)", visible, re.I)),
          "Page visibly identifies Local preview mode")
    check("mick" in visible.lower(), "Library renders the project branding")

    for path in ("/Studio", "/Admin"):
        status, _, _ = client.request(path, redirect=False)
        check(status in (302, 303, 401), f"Anonymous {path} requires authentication")

    status, _, _ = client.request("/Episodes/Play/1", {})
    check(status == 400, "Write endpoint rejects a missing anti-forgery token")
    attack = '<script>alert("smoke")</script>'
    search = client.get("/Episodes?" + urlencode({"topic": attack, "host": "' OR 1=1 --"}))
    check(attack not in search, "Search input is not emitted as executable HTML")

    marker = uuid.uuid4().hex[:10]
    email = f"smoke-{marker}@mick.local"
    password = "SmokeCheck!42"
    registration = Page(client.get("/Account/Register"))
    status, body, _ = client.post(
        "/Account/Register", registration.token(), DisplayName=f"Smoke {marker}",
        Email=email, Password=password, ConfirmPassword=password,
    )
    check(status == 200, "Registration completes without a server error")
    profile = client.get("/Account/Profile")
    check(email.lower() in profile.lower() or f"Smoke {marker}" in profile,
          "Registered Listener has an authenticated profile")

    for path in ("/Studio", "/Admin"):
        status, _, final = client.request(path)
        check(status == 403 or "accessdenied" in final.lower() or "denied" in final.lower(),
              f"Listener cannot access {path}")

    detail = Page(client.get("/Episodes/Details/1"))
    note = f"Smoke note {marker} <script>alert('smoke')</script>"
    status, body, _ = client.post("/Episodes/Comment/1", detail.token(), text=note)
    check(status == 200, "Listener can add a comment")
    detail_source = client.get("/Episodes/Details/1")
    check(f"Smoke note {marker}" in detail_source and "&lt;script&gt;" in detail_source,
          "Saved comment is listed with escaped markup")
    detail = Page(detail_source)
    own_link = next((link for link in detail.links if "editcomment" in link.lower()), None)
    check(own_link is not None, "Recent own comment exposes an edit action")
    edit = Page(client.get(own_link))
    fields = {key: value for key, value in edit.inputs.items()
              if key != "__RequestVerificationToken"}
    fields["Text"] = f"Revised smoke note {marker}"
    status, _, _ = client.post("/Episodes/EditComment", edit.token(), **fields)
    check(status == 200 and fields["Text"] in client.get("/Episodes/Details/1"),
          "Author can edit a recent comment")

    token = Page(client.get("/Episodes/Details/1")).token()
    status, body, _ = client.post("/Episodes/Play/1", token)
    check(status == 200, "Episode play is accepted")
    first = json.loads(body)["playCount"]
    status, body, _ = client.post("/Episodes/Play/1", token)
    check(status == 200 and json.loads(body)["playCount"] == first + 1,
          "Playing increments the persisted view count")

    current = Page(client.get("/Account/Profile"))
    client.post("/Account/Logout", current.token())
    login = Page(client.get("/Account/Login"))
    status, _, _ = client.post("/Account/Login", login.token(), Email=email, Password=password)
    check(status == 200 and email.lower() in client.get("/Account/Profile").lower(),
          "Newly registered credentials work after logout")

    print(f"Completed Local HTTP smoke checks. Added account {email} and one comment.")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"FAIL {error}", file=sys.stderr)
        sys.exit(1)
