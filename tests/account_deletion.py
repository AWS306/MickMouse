#!/usr/bin/env python3
"""Account deletion checks; use only with the disposable Local test runner."""
import datetime
import json
from pathlib import Path
import sqlite3
import sys
import uuid
from urllib.parse import urlparse
from http_smoke import Client, Page, check as assert_check
from episode_flow import multipart, login

count = 0
def check(condition, message):
    global count
    assert_check(condition, message)
    count += 1

def main():
    base, database = sys.argv[1:]
    check(urlparse(base).hostname in {"localhost", "127.0.0.1", "::1"}, "Account deletion target is loopback")
    anon = Client(base)
    check("Local preview" in anon.get("/Episodes"), "Account deletion runs in Local preview")
    db = sqlite3.connect(Path(database).resolve(strict=True))
    db.row_factory = sqlite3.Row
    def scalar(sql, *values): return db.execute(sql, values).fetchone()[0]
    def token(client, path="/Account/Profile"): return Page(client.get(path)).token()
    def notes(): return json.loads((Path(database).parent / "comments.json").read_text())
    password = "DeleteCheck!42"
    marker = uuid.uuid4().hex[:10]
    admin = login(base, "admin@mick.local")
    admin_id = "10000000-0000-0000-0000-000000000001"

    check(anon.request("/Account/DeleteAccount", redirect=False)[0] in (302,401), "Anonymous account deletion requires login")
    status, _, _ = admin.post("/Account/DeleteAccount", token(admin), Password="TrailDemo!42", Confirmation="delete my account")
    check(status == 403 and scalar('SELECT COUNT(*) FROM "AspNetUsers" WHERE "Id"=?', admin_id) == 1,
          "Last active admin cannot delete their account")

    def register(prefix):
        client = Client(base); email = f"{prefix}-{marker}@mick.local"
        status, _, _ = client.post("/Account/Register", token(client,"/Account/Register"),
                                  DisplayName=prefix, Email=email, Password=password, ConfirmPassword=password)
        check(status == 200 and email in client.get("/Account/Profile"), "Deletion fixture account registers")
        identity = scalar('SELECT "Id" FROM "AspNetUsers" WHERE "Email"=?', email)
        return client, email, identity

    listener, email, identity = register("DeleteListener")
    check("Delete My Account" in listener.get("/Account/Profile"), "My Account exposes Delete My Account")
    check("Current password" in listener.get("/Account/DeleteAccount"), "Deletion page requires current password")
    old_session = Client(base)
    old_session.post("/Account/Login",token(old_session,"/Account/Login"),Email=email,Password=password)
    for episode in (1,5):
        listener.post(f"/Episodes/Comment/{episode}",token(listener,f"/Episodes/Details/{episode}"),text=f"Deletion check {marker}")
    listener.post("/Episodes/Like/2",token(listener,"/Episodes/Details/2"))
    listener.post("/Podcasts/Subscribe/2",token(listener))
    check(sum(note["userId"].lower()==identity.lower() for note in notes())==2, "Author has comments in two shows")
    check(scalar('SELECT COUNT(*) FROM "EpisodeLikes" WHERE "UserId"=?',identity)==1, "Deletion fixture has a like")
    check(scalar('SELECT COUNT(*) FROM "Subscriptions" WHERE "UserId"=?',identity)==1, "Deletion fixture has a subscription")
    status, _, _ = listener.request("/Account/DeleteAccount",{"Password":password,"Confirmation":"delete my account"})
    check(status==400, "Account deletion rejects missing anti-forgery protection")
    status, body, _ = listener.post("/Account/DeleteAccount",token(listener,"/Account/DeleteAccount"),Password=password,Confirmation="delete")
    check(status==200 and "exactly as shown" in body, "Wrong confirmation phrase is rejected")
    check(scalar('SELECT COUNT(*) FROM "AspNetUsers" WHERE "Id"=?',identity)==1, "Wrong phrase preserves account")
    status, body, _ = listener.post("/Account/DeleteAccount",token(listener,"/Account/DeleteAccount"),Password="WrongPassword!42",Confirmation="delete my account")
    check(status==200 and "password could not be verified" in body, "Wrong password is rejected")
    check(scalar('SELECT COUNT(*) FROM "AspNetUsers" WHERE "Id"=?',identity)==1, "Wrong password preserves account")
    status, body, _ = listener.post("/Account/DeleteAccount",token(listener,"/Account/DeleteAccount"),Password=password,Confirmation="delete my account",userId=admin_id)
    check(status==200 and "have been deleted" in body, "Confirmed account deletion completes")
    check(scalar('SELECT COUNT(*) FROM "AspNetUsers" WHERE "Id"=?',identity)==0, "Deleted account is removed from SQL")
    check(scalar('SELECT COUNT(*) FROM "AspNetUsers" WHERE "Id"=?',admin_id)==1, "Forged target ID cannot delete another user")
    check(not any(note["userId"].lower()==identity.lower() for note in notes()), "All deleted user's comments are removed")
    check(scalar('SELECT COUNT(*) FROM "EpisodeLikes" WHERE "UserId"=?',identity)==0, "Deleted user's likes are removed")
    check(scalar('SELECT COUNT(*) FROM "Subscriptions" WHERE "UserId"=?',identity)==0, "Deleted user's subscriptions are removed")
    check("/Account/Login" in listener.request("/Account/Profile")[2], "Deleting session is signed out")
    check("/Account/Login" in old_session.request("/Account/Profile")[2], "Other existing sessions lose access")
    new_login = Client(base)
    new_login.post("/Account/Login",token(new_login,"/Account/Login"),Email=email,Password=password)
    check("/Account/Login" in new_login.request("/Account/Profile")[2], "Deleted credentials cannot sign in")

    owner, owner_email, owner_id = register("DeleteOwner")
    admin.post("/Admin/ChangeRole",token(admin,"/Admin"),userId=owner_id,role="Podcaster")
    owner.post("/Account/Login",token(owner,"/Account/Login"),Email=owner_email,Password=password)
    title="Delete owned show "+marker
    owner.post("/Studio/CreatePodcast",token(owner,"/Studio/CreatePodcast"),Title=title,Description="Deletion fixture",HostName="Deletion Host",Topic="Tests",CoverKey="systems")
    show_id=scalar('SELECT "Id" FROM "Podcasts" WHERE "Title"=?',title)
    audio=(Path(__file__).resolve().parents[1]/"src/MickMouse.Web/wwwroot/media/samples/episode-01.mp3").read_bytes()
    status, _, _=multipart(owner,"/Studio/CreateEpisode",token(owner,"/Studio/CreateEpisode"),
                          {"PodcastId":show_id,"Title":"Owned recording "+marker,"Summary":"Disposable deletion check", "Topic":"Tests","CoverKey":"systems","DurationMinutes":1,
                           "ReleaseDateUtc":(datetime.datetime.now(datetime.timezone.utc).date()-datetime.timedelta(days=1)).isoformat()},audio)
    episode=db.execute('SELECT * FROM "Episodes" WHERE "PodcastId"=?',(show_id,)).fetchone()
    check(status==200 and episode is not None, "Owner deletion fixture includes an uploaded episode")
    upload=Path(database).parent/episode["MediaKey"]
    check(upload.is_file(), "Owned upload exists before deletion")
    admin.post(f"/Admin/Approve/{episode['Id']}",token(admin,"/Admin"))
    alex=login(base,"alex@mick.local")
    alex.post(f"/Episodes/Comment/{episode['Id']}",token(alex,f"/Episodes/Details/{episode['Id']}"),text="A listener on a removable show")
    alex.post(f"/Podcasts/Subscribe/{show_id}",token(alex))
    owner.post("/Episodes/Comment/1",token(owner,"/Episodes/Details/1"),text="Owner comment on someone else's show")
    status, _, _=owner.post("/Account/DeleteAccount",token(owner,"/Account/DeleteAccount"),Password=password,Confirmation="delete my account")
    check(status==200, "Owner can delete an account with owned content")
    check(scalar('SELECT COUNT(*) FROM "AspNetUsers" WHERE "Id"=?',owner_id)==0, "Owner account is removed")
    check(scalar('SELECT COUNT(*) FROM "Podcasts" WHERE "Id"=?',show_id)==0, "Owned podcast is removed")
    check(scalar('SELECT COUNT(*) FROM "Episodes" WHERE "Id"=?',episode["Id"])==0, "Owned episode is removed")
    check(not upload.exists(), "Owned uploaded recording is removed")
    check(not any(note["userId"].lower()==owner_id.lower() or note["episodeId"]==episode["Id"] for note in notes()), "Owner's comments and removed-episode discussion are cleaned")
    check(scalar('SELECT COUNT(*) FROM "Subscriptions" WHERE "PodcastId"=?',show_id)==0, "Subscriptions to removed show are cleaned")
    check(scalar('SELECT COUNT(*) FROM "AspNetUsers" WHERE "Email"=?',"alex@mick.local")==1, "Other listeners retain their accounts")
    check(scalar('SELECT COUNT(*) FROM "Episodes" WHERE "Id"<=10 AND "IsDeleted"=0')==10, "Other shows and all original episodes remain")
    check((Path(database).parent/"samples/episode-01.mp3").is_file(), "Shared sample recording remains")
    print(f"Completed {count} account deletion assertions.")

if __name__=="__main__":
    try: main()
    except Exception as error:
        print(f"FAIL {error}",file=sys.stderr);sys.exit(1)
