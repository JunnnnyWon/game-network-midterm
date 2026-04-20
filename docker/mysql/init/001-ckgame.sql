CREATE DATABASE IF NOT EXISTS ckgame CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE ckgame;

CREATE TABLE IF NOT EXISTS match_results (
  match_id VARCHAR(64) NOT NULL PRIMARY KEY,
  room_id VARCHAR(32) NOT NULL,
  ended_at_utc DATETIME(6) NOT NULL,
  end_reason VARCHAR(32) NOT NULL,
  winner_player_name VARCHAR(64) NULL,
  player_count INT NOT NULL,
  player_a_name VARCHAR(64) NOT NULL,
  player_a_score INT NOT NULL,
  player_b_name VARCHAR(64) NULL,
  player_b_score INT NULL,
  raw_payload_json JSON NOT NULL
);

CREATE TABLE IF NOT EXISTS player_stats (
  player_name VARCHAR(64) NOT NULL PRIMARY KEY,
  wins INT NOT NULL DEFAULT 0,
  draws INT NOT NULL DEFAULT 0,
  losses INT NOT NULL DEFAULT 0,
  best_score INT NOT NULL DEFAULT 0,
  total_matches INT NOT NULL DEFAULT 0,
  last_played_at DATETIME(6) NOT NULL
);
