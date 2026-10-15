#!/usr/bin/env python3
"""Pay players for items removed from the game, once, with every server STOPPED.

WHY: on 2026-10-16 each creature went down to one material and the three headgear pieces with no 3D model were
removed (tools/deleted-items-2026-10-16.json lists every removed item and the value it had). A save that still names
one of them would lose it silently on load. This takes every such item out of every character's bag and worn slots
and out of every account's storage, and pays its old shop sell price (value less Tax, ShopMath) into a purse:
the holder's own for a bag or a worn piece, the account's most recently played character for storage. A buy-back
entry is only removed: the shop already paid for that sale.

RUN IT ONLY WITH THE MAP SERVERS AND THE CHARACTER SERVER STOPPED, and after checking that no map server has unsent
saves waiting on disk — a map server keeps those and sends them first when it starts again, which would write the old
bag back over this.

USAGE:   tools/pay-out-deleted-items.py            # dry run: says what it would do, changes nothing
         tools/pay-out-deleted-items.py --apply    # does it, in one transaction
EXIT:    0 = done (or nothing to do); 1 = refused or failed.
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
LIST = os.path.join(HERE, "deleted-items-2026-10-16.json")


def database_url():
    for line in open(os.path.join(REPO, "settings.env"), encoding="utf-8"):
        if line.startswith("DATABASE_URL="):
            return line.split("=", 1)[1].strip()
    raise SystemExit("settings.env has no DATABASE_URL")


def psql(url, sql, **vars):
    args = ["psql", url, "-X", "-q", "-A", "-t", "-v", "ON_ERROR_STOP=1"]
    for k, v in vars.items():
        args += ["-v", "%s=%s" % (k, v)]
    p = subprocess.run(args, input=sql, capture_output=True, text=True, encoding="utf-8")
    if p.returncode != 0:
        raise SystemExit("psql failed: " + p.stderr.strip())
    return p.stdout


def tax_each(value):
    # ShopMath.TaxEach: a third of the value, rounded up; nothing on a value of 0.
    return 0 if value <= 0 else (value + 2) // 3


def sell_price(value):
    return max(0, value - tax_each(value))


def drop_parallel(doc, keys, ids_key, gone):
    """Remove every index whose id is in `gone` from the parallel arrays `keys`; returns [(id, count)] removed."""
    ids = doc.get(ids_key) or []
    keep = [i for i, x in enumerate(ids) if x not in gone]
    removed = [(ids[i], (doc.get("bagCounts") or [1] * len(ids))[i] if ids_key == "bagItemIds" else 1)
               for i in range(len(ids)) if ids[i] in gone]
    if removed:
        for k in keys:
            arr = doc.get(k)
            if isinstance(arr, list) and len(arr) == len(ids):
                doc[k] = [arr[i] for i in keep]
    return removed


def main():
    apply = "--apply" in sys.argv
    gone = {x["id"]: x for x in json.load(open(LIST, encoding="utf-8"))}
    url = database_url()
    rows = psql(url, "select id, account_id, progress::text from character where deleted_at is null;")
    bag_keys = ["bagItemIds", "bagCounts", "bagRefines", "bagCards", "bagRolls", "bagCopyIds"]
    worn_keys = ["equipSlots", "equipItemIds", "equipRefines", "equipCards", "equipRolls", "equipCopyIds"]
    updates, said = [], []
    for row in rows.splitlines():
        if not row.strip():
            continue
        cid, account, text = row.split("|", 2)
        doc = json.loads(text) if text else {}
        paid = 0
        for item, count in drop_parallel(doc, bag_keys, "bagItemIds", gone) + drop_parallel(doc, worn_keys, "equipItemIds", gone):
            paid += sell_price(gone[item]["value"]) * count
            said.append("%s: %d x %s -> %d" % (cid, count, gone[item]["name"], sell_price(gone[item]["value"]) * count))
        buyback = doc.get("buyback") or []
        kept = [b for b in buyback if b.get("itemId") not in gone]
        if len(kept) != len(buyback):
            said.append("%s: %d buy-back entr(ies) removed, nothing owed" % (cid, len(buyback) - len(kept)))
            doc["buyback"] = kept
        if paid or len(kept) != len(buyback) or doc.get("bagItemIds") != json.loads(text or "{}").get("bagItemIds") \
                or doc.get("equipItemIds") != json.loads(text or "{}").get("equipItemIds"):
            doc["gold"] = int(doc.get("gold") or 0) + paid
            updates.append((cid, json.dumps(doc, ensure_ascii=False)))
    ids = ",".join("'%s'" % i.replace("'", "''") for i in gone)
    stored = psql(url, "select account_id, item_id, count from account_storage where item_id in (%s);" % ids)
    store_pay = {}
    for row in stored.splitlines():
        if not row.strip():
            continue
        account, item, count = row.split("|")
        store_pay[account] = store_pay.get(account, 0) + sell_price(gone[item]["value"]) * int(count)
        said.append("account %s storage: %s x %s" % (account, count, gone[item]["name"]))

    for line in said:
        print(line)
    print("%d character(s) to rewrite, %d account(s) paid for storage" % (len(updates), len(store_pay)))
    if not apply:
        print("dry run — nothing changed. Run again with --apply, with every server stopped.")
        return 0

    sql = ["begin;"]
    for cid, doc in updates:
        sql.append("update character set progress = %s::jsonb where id = '%s';" % ("$doc$" + doc + "$doc$", cid))
    for account, amount in store_pay.items():
        sql.append(("update character set progress = jsonb_set(progress, '{gold}', to_jsonb(coalesce((progress->>'gold')::bigint, 0) + %d)) "
                    "where id = (select id from character where account_id = '%s' and deleted_at is null order by updated_at desc limit 1);")
                   % (amount, account))
    sql.append("delete from account_storage where item_id in (%s);" % ids)
    sql.append("update account_storage_head set revision = revision + 1 where account_id in (%s);"
               % (",".join("'%s'" % a for a in store_pay) or "null"))
    sql.append("commit;")
    psql(url, "\n".join(sql))
    print("done.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
