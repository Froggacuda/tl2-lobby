@echo off
REM ============================================================
REM  TL2 Community Lobby — launcher (sample)
REM  Project / source / updates:  https://github.com/Froggacuda/tl2-lobby
REM ============================================================
REM  Rename your built/published exe to tl2lobby.exe (or edit the
REM  last line), drop this file next to it, and run it.
REM
REM  SETUP:
REM   1. Port-forward TCP + UDP 4549 to this machine on your router.
REM   2. Friends set  LOBBYHOST :<your public IP or domain>  in their
REM      Torchlight 2 local_settings.txt  (LOBBYPORT :4549).
REM   3. SELF-HOSTING (you host a GAME on the same PC/LAN as this lobby):
REM      set TL2_RELAY_IP below to YOUR public IP so co-located games
REM      are reachable by remote friends. Remote-only games don't need
REM      it. Leave it commented out if you're unsure.
REM ------------------------------------------------------------
REM set TL2_RELAY_IP=203.0.113.10

REM Clean operational logging by default. Add -debug for the verbose
REM per-message / RE diagnostic firehose.
tl2lobby.exe > server.log 2>&1
