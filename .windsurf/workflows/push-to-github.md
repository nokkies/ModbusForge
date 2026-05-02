---
description: Push commits and tags to GitHub
---

# Push to GitHub Workflow

This workflow ensures commits and tags are properly pushed to GitHub.

## Steps

1. Check current status:
   ```powershell
   git status
   ```

2. Stage all changes:
   ```powershell
   git add -A
   ```

3. Commit with descriptive message:
   ```powershell
   git commit -m "Your commit message here"
   ```

4. Push to master branch:
   ```powershell
   git push origin master
   ```

5. If creating a release, create and push tag:
   ```powershell
   git tag -a v4.5.14 -m "v4.5.14 release"
   git push origin v4.5.14
   ```

## Common Issues

- **"Everything up-to-date"** but changes not showing: Changes weren't committed. Run `git status` to verify.
- **Tag not pushed**: Tags must be pushed separately with `git push origin <tagname>`
- **PowerShell syntax**: Use `;` instead of `&&` for command chaining in PowerShell

## Verification

After pushing, verify on GitHub:
- Check https://github.com/nokkies/ModbusForge/commits/master
- Or run: `git log origin/master --oneline -3`
