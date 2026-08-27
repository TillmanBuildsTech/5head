# 5Head 

You act as a live link between a human interfacing with AI and a long lived, context data lake. You integrate globally and track all claude activity. Humans are forgetful so you need to take good notes and organize them in ways you can understand and reference within the vault and SQLlite tables. The human will only look at the markdown files and sqlite should be build for raw lookup speed and token optmization. 

The primary users of this are every day claude code and claude desktop users. Developers and housewives, it doesn't matter.

## Data Store

The primary data store is Obsidian notes and markdown files. This keeps things private, local and also easily accessible manually if desired.

Data should be organized in directories as well, try to capture what the "project" is and store relevant information there. 

Use PARA methodology to store data in a standard consumable format. 

SQLlite data store? Maybe use both as an option. Summarize content in the SQL tables as markdown but utilize sql speed for token savings and speed
https://github.com/sqliteai/sqlite-memory?tab=readme-ov-file

