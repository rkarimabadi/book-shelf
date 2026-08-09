import sqlite3, sys

path = sys.argv[1]
con = sqlite3.connect(path)
cur = con.cursor()
print("tables:", cur.execute("select name from sqlite_master where type='table'").fetchall())
for t in ("Users", "OutboxMessages", "RefreshToken"):
    try:
        print(t, cur.execute(f"select count(*) from {t}").fetchall())
    except Exception as e:
        print(t, "ERR", e)
cur.execute("select Id, Email, IsActive from Users")
print("user rows:", cur.fetchall())
con.close()
