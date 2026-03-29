-- ============================================================
--  FleetOS — Seed Data
--  20 Vehicles · 20 Drivers · 20 Routes (Germany)
-- ============================================================

-- ── Drivers ──────────────────────────────────────────────────

INSERT INTO public.fms_drivers (id, first_name, last_name, license_number, phone_number, email, is_available, hours_worked, max_hours_per_day, created_at) VALUES
  ('0345AB6A-D8E5-46D5-BAB5-0C62694538CC', 'Klaus',     'Müller',      'DE-LIC-10001', '+49 30 11122201', 'k.mueller@fleetos.de',      true,  2.5,  9.0, NOW()),
  ('e88f746e-801a-4f1d-a21f-d35aebbdd0b6', 'Sabine',    'Schmidt',     'DE-LIC-10002', '+49 89 11122202', 's.schmidt@fleetos.de',      true,  0.0,  8.0, NOW()),
  ('dabe68fd-66dd-4835-9264-ae99bdecd1f5', 'Thomas',    'Becker',      'DE-LIC-10003', '+49 40 11122203', 't.becker@fleetos.de',       false, 7.5,  8.0, NOW()),
  ('f94fec69-9082-40f7-9c95-d6da9720a1f2', 'Petra',     'Hoffmann',    'DE-LIC-10004', '+49 221 1112204', 'p.hoffmann@fleetos.de',     true,  1.0,  9.0, NOW()),
  ('6400b736-8cb7-4a75-95df-c04e340e7f6d', 'Andreas',   'Wagner',      'DE-LIC-10005', '+49 711 1112205', 'a.wagner@fleetos.de',       true,  3.0,  8.0, NOW()),
  ('f72940fb-d332-427b-9eb1-e4c180e003f1', 'Monika',    'Fischer',     'DE-LIC-10006', '+49 351 1112206', 'm.fischer@fleetos.de',      false, 8.0,  8.0, NOW()),
  ('fe835d5a-f655-45d4-8631-72b06a5fa409', 'Stefan',    'Weber',       'DE-LIC-10007', '+49 511 1112207', 's.weber@fleetos.de',        true,  4.5,  9.0, NOW()),
  ('9984a9d2-f1d1-40ec-9668-97f8afe40480', 'Ursula',    'Meyer',       'DE-LIC-10008', '+49 341 1112208', 'u.meyer@fleetos.de',        true,  0.0,  8.0, NOW()),
  ('d7f29d51-5301-4c59-9192-7dca104ceb0d', 'Frank',     'Schulz',      'DE-LIC-10009', '+49 911 1112209', 'f.schulz@fleetos.de',       true,  6.0,  9.0, NOW()),
  ('574ae822-e42b-4328-8337-52301b8a7e76', 'Heike',     'Zimmermann',  'DE-LIC-10010', '+49 621 1112210', 'h.zimmermann@fleetos.de',   false, 8.5,  8.0, NOW()),
  ('f8a11da5-2cd4-441c-96b5-cfa69be50177', 'Jürgen',    'Braun',       'DE-LIC-10011', '+49 761 1112211', 'j.braun@fleetos.de',        true,  1.5,  8.0, NOW()),
  ('33200fbf-6b02-40a4-bec0-b0bb7b15ef32', 'Claudia',   'Krause',      'DE-LIC-10012', '+49 431 1112212', 'c.krause@fleetos.de',       true,  2.0,  9.0, NOW()),
  ('97c64bed-b39c-430f-a183-a72c480f91d7', 'Markus',    'Heinrich',    'DE-LIC-10013', '+49 631 1112213', 'm.heinrich@fleetos.de',     true,  0.0,  8.0, NOW()),
  ('38719778-8153-41d0-96a7-19b9d70b9b72', 'Birgit',    'Richter',     'DE-LIC-10014', '+49 201 1112214', 'b.richter@fleetos.de',      false, 7.0,  8.0, NOW()),
  ('eeeeb28e-861c-40ec-8f09-e1a36bd93f54', 'Dieter',    'Klein',       'DE-LIC-10015', '+49 421 1112215', 'd.klein@fleetos.de',        true,  3.5,  9.0, NOW()),
  ('2d6be49c-0a14-41ca-8e93-99b94c0fa4c9', 'Renate',    'Wolf',        'DE-LIC-10016', '+49 911 1112216', 'r.wolf@fleetos.de',         true,  5.0,  8.0, NOW()),
  ('0ab6aa67-85d6-4a2f-ac04-f6a300692272', 'Michael',   'Schäfer',     'DE-LIC-10017', '+49 711 1112217', 'm.schaefer@fleetos.de',     true,  1.0,  9.0, NOW()),
  ('678b664f-a20e-46b1-9849-9e063d69d17d', 'Ingrid',    'König',       'DE-LIC-10018', '+49 89  1112218', 'i.koenig@fleetos.de',       false, 8.0,  8.0, NOW()),
  ('07c28f0f-d9bb-4749-b037-241f8b2de8d4', 'Werner',    'Lange',       'DE-LIC-10019', '+49 30  1112219', 'w.lange@fleetos.de',        true,  4.0,  8.0, NOW()),
  ('680edbb4-aa3c-40ed-bcec-45dc7db0be2d', 'Anneliese', 'Schwarz',     'DE-LIC-10020', '+49 221 1112220', 'a.schwarz@fleetos.de',      true,  0.5,  9.0, NOW());

-- ── Vehicles ──────────────────────────────────────────────────
-- lat/lon: major German cities and logistics hubs

INSERT INTO public.fms_vehicles (id, license_plate, vehicle_type, status, lat, lon,
  assigned_driver_id, fuel_level_pct, speed_kmh, max_payload_kg,
  current_payload_kg, odometer_km, engine_temp, battery_level,
  last_heartbeat, diagnostic_codes, created_at, updated_at) VALUES

  -- Munich area
  ('5c4216b9-b03c-4d97-85de-99be6a2fd7ba', 'M-FL-0001',  'Truck',       'EnRoute',     48.1351,  11.5820,
   '0345AB6A-D8E5-46D5-BAB5-0C62694538CC', 62.0,  87.0, 20000.0,  12000.0, 145320.0, 89.0, NULL, NOW(), '[]', NOW(), NOW()),

  ('f2424ec7-a80d-4529-80ba-159652e3f7c7', 'M-FL-0002',  'Van',         'Idle',        48.1550,  11.6010,
   'e88f746e-801a-4f1d-a21f-d35aebbdd0b6', 88.5,   0.0,  3500.0,      0.0,  42100.0, 22.0, NULL, NOW(), '[]', NOW(), NOW()),

  -- Berlin area
  ('12aedea1-7e13-4e54-9c9a-622e0194b448', 'B-FL-0003',  'Truck',       'EnRoute',     52.5200,  13.4050,
   'dabe68fd-66dd-4835-9264-ae99bdecd1f5', 45.0,  72.0, 18000.0,   9500.0, 231400.0, 91.0, NULL, NOW(), '[]', NOW(), NOW()),

  ('e7cd7240-9994-47f5-bd91-e168d3597c98', 'B-FL-0004',  'Van',         'Maintenance', 52.4900,  13.3800,
   NULL,                                   15.5,   0.0,  3500.0,      0.0,  88700.0, 95.0, NULL, NOW(), '["P0401"]', NOW(), NOW()),

  -- Hamburg area
  ('c2b86f44-02aa-4093-912c-a983acf7f5e8', 'HH-FL-0005', 'Truck',       'Idle',        53.5511,   9.9937,
   'f94fec69-9082-40f7-9c95-d6da9720a1f2', 95.0,   0.0, 24000.0,      0.0,  67800.0, 21.0, NULL, NOW(), '[]', NOW(), NOW()),

  ('ab04ec51-49dd-467e-aa23-8ec1b0958e1c', 'HH-FL-0006', 'ElectricVan', 'Charging',    53.5800,  10.0200,
   '6400b736-8cb7-4a75-95df-c04e340e7f6d', 34.0,   0.0,  2800.0,      0.0,  19200.0, 28.0, 34.0, NOW(), '[]', NOW(), NOW()),

  -- Frankfurt area
  ('52ea015e-b6c0-4064-8a65-a5459245bb21', 'F-FL-0007',  'Truck',       'EnRoute',     50.1109,   8.6821,
   'fe835d5a-f655-45d4-8631-72b06a5fa409', 71.0,  94.0, 20000.0,  15000.0, 310200.0, 93.0, NULL, NOW(), '[]', NOW(), NOW()),

  ('038ac04e-6cf7-439f-bff1-f7e45363d1ec', 'F-FL-0008',  'Car',         'Idle',        50.1200,   8.7100,
   '9984a9d2-f1d1-40ec-9668-97f8afe40480', 56.0,   0.0,    600.0,      0.0,  33500.0, 20.0, NULL, NOW(), '[]', NOW(), NOW()),

  -- Cologne area
  ('6478481c-f106-4ad0-86ad-354838cb4ca7', 'K-FL-0009',  'Truck',       'EnRoute',     50.9333,   6.9600,
   'd7f29d51-5301-4c59-9192-7dca104ceb0d', 53.0,  81.0, 22000.0,  18000.0, 195600.0, 88.0, NULL, NOW(), '[]', NOW(), NOW()),

  ('74bc9660-5147-491b-846c-54f3f084313b', 'K-FL-0010',  'Van',         'OutOfService',50.9000,   6.9200,
   NULL,                                    8.0,   0.0,  3500.0,      0.0, 421000.0,102.0, NULL, NOW(), '["P0300","P0171"]', NOW(), NOW()),

  -- Stuttgart area
  ('3bea21d1-0d38-42c5-97fc-f8073d67aad0', 'S-FL-0011',  'ElectricTruck','EnRoute',    48.7758,   9.1829,
   'f8a11da5-2cd4-441c-96b5-cfa69be50177', 78.0,  65.0, 15000.0,   8000.0,  28400.0, 42.0, 78.0, NOW(), '[]', NOW(), NOW()),

  ('565c34e6-502e-4742-b1df-da2c98bdf13d', 'S-FL-0012',  'Van',         'Idle',        48.7900,   9.2100,
   '33200fbf-6b02-40a4-bec0-b0bb7b15ef32', 100.0,  0.0,  3500.0,      0.0,  11200.0, 20.0, NULL, NOW(), '[]', NOW(), NOW()),

  -- Dortmund area
  ('fd5aac6a-95da-454a-8d62-8bb9dc953577', 'DO-FL-0013', 'Truck',       'Idle',        51.5136,   7.4653,
   '97c64bed-b39c-430f-a183-a72c480f91d7', 82.0,   0.0, 20000.0,      0.0, 158900.0, 22.0, NULL, NOW(), '[]', NOW(), NOW()),

  ('7b6ef527-75a5-4e9b-874f-715811b6b5f4', 'DO-FL-0014', 'Motorcycle',  'EnRoute',     51.5300,   7.4900,
   'eeeeb28e-861c-40ec-8f09-e1a36bd93f54', 48.0, 112.0,    120.0,     60.0,  74300.0, 85.0, NULL, NOW(), '[]', NOW(), NOW()),

  -- Leipzig area
  ('90e2e457-b15b-4c99-81bb-2111d8cd2ca5', 'L-FL-0015',  'Truck',       'EnRoute',     51.3397,  12.3731,
   '2d6be49c-0a14-41ca-8e93-99b94c0fa4c9', 67.0,  76.0, 18000.0,  11000.0, 203100.0, 90.0, NULL, NOW(), '[]', NOW(), NOW()),

  ('112c1b54-4bde-4352-b655-dc321472f42e', 'L-FL-0016',  'Van',         'Idle',        51.3600,  12.3900,
   '0ab6aa67-85d6-4a2f-ac04-f6a300692272', 91.0,   0.0,  3500.0,      0.0,  55400.0, 21.0, NULL, NOW(), '[]', NOW(), NOW()),

  -- Nuremberg area
  ('33928982-86e8-4a93-9149-3b5ec5767ea4', 'N-FL-0017',  'Truck',       'Maintenance', 49.4521,  11.0767,
   NULL,                                   22.0,   0.0, 20000.0,      0.0, 387600.0, 97.0, NULL, NOW(), '["P0420"]', NOW(), NOW()),

  -- Bremen area
  ('9244c42d-7f2e-4857-9f5b-f5025b0e27a8', 'HB-FL-0018', 'ElectricVan', 'EnRoute',     53.0793,   8.8017,
   '07c28f0f-d9bb-4749-b037-241f8b2de8d4', 55.0,  58.0,  2800.0,   1400.0,  37800.0, 38.0, 55.0, NOW(), '[]', NOW(), NOW()),

  -- Hannover area
  ('b013f5a1-de36-43a2-a77d-b668a8160c81', 'H-FL-0019',  'Van',         'Idle',        52.3759,   9.7320,
   '680edbb4-aa3c-40ed-bcec-45dc7db0be2d', 74.0,   0.0,  3500.0,      0.0,  62100.0, 20.0, NULL, NOW(), '[]', NOW(), NOW()),

  -- Dresden area
  ('34f3c84f-90fa-46e1-ada8-74bb8cf7f155', 'DD-FL-0020', 'Truck',       'EnRoute',     51.0504,  13.7373,
   'd7f29d51-5301-4c59-9192-7dca104ceb0d', 39.0,  69.0, 22000.0,  14000.0, 276500.0, 92.0, NULL, NOW(), '[]', NOW(), NOW());

-- ── Routes (Germany) ──────────────────────────────────────────
-- Waypoints as JSONB, optimized path as JSONB array of node IDs

INSERT INTO public.fms_routes (id, vehicle_id, driver_id, total_distance_km, estimated_duration_min,
  status, priority, algorithm, waypoints_json, optimized_path_json,
  created_at, started_at, completed_at) VALUES

  -- 1. Munich → Nuremberg (A9)
  ('b1000001-0000-0000-0000-000000000001',
   '5c4216b9-b03c-4d97-85de-99be6a2fd7ba', '0345AB6A-D8E5-46D5-BAB5-0C62694538CC',
   171.0, 102,  'Active', 'Normal', 'AStar',
   '[{"nodeId":"MUC-DEPOT","lat":48.1351,"lon":11.5820,"address":"Munich Central Depot"},{"nodeId":"ING-HUB","lat":48.7665,"lon":11.4258,"address":"Ingolstadt Logistics Hub"},{"nodeId":"NUE-DEPOT","lat":49.4521,"lon":11.0767,"address":"Nuremberg Distribution Centre"}]',
   '["MUC-DEPOT","ING-HUB","NUE-DEPOT"]',
   NOW() - INTERVAL '2 hours', NOW() - INTERVAL '2 hours', NULL),

  -- 2. Berlin → Leipzig (A9)
  ('b1000001-0000-0000-0000-000000000002',
   '12aedea1-7e13-4e54-9c9a-622e0194b448', 'dabe68fd-66dd-4835-9264-ae99bdecd1f5',
   188.0, 112,  'Active', 'High', 'Dijkstra',
   '[{"nodeId":"BER-DEPOT","lat":52.5200,"lon":13.4050,"address":"Berlin East Depot"},{"nodeId":"WB-STOP","lat":51.8667,"lon":12.6333,"address":"Wittenberg Stop"},{"nodeId":"LEI-DEPOT","lat":51.3397,"lon":12.3731,"address":"Leipzig North Depot"}]',
   '["BER-DEPOT","WB-STOP","LEI-DEPOT"]',
   NOW() - INTERVAL '1 hour 30 minutes', NOW() - INTERVAL '1 hour 30 minutes', NULL),

  -- 3. Hamburg → Bremen (A1)
  ('b1000001-0000-0000-0000-000000000003',
   'c2b86f44-02aa-4093-912c-a983acf7f5e8', 'f94fec69-9082-40f7-9c95-d6da9720a1f2',
   119.0,  80,  'Active', 'Normal', 'AStar',
   '[{"nodeId":"HAM-DEPOT","lat":53.5511,"lon":9.9937,"address":"Hamburg Port Depot"},{"nodeId":"BRE-DEPOT","lat":53.0793,"lon":8.8017,"address":"Bremen Logistics Park"}]',
   '["HAM-DEPOT","BRE-DEPOT"]',
   NOW() - INTERVAL '50 minutes', NOW() - INTERVAL '50 minutes', NULL),

  -- 4. Frankfurt → Cologne (A3)
  ('b1000001-0000-0000-0000-000000000004',
   '52ea015e-b6c0-4064-8a65-a5459245bb21', 'fe835d5a-f655-45d4-8631-72b06a5fa409',
   189.0, 113,  'Active', 'Emergency', 'BellmanFord',
   '[{"nodeId":"FRA-DEPOT","lat":50.1109,"lon":8.6821,"address":"Frankfurt Airport Logistics"},{"nodeId":"WIE-STOP","lat":50.5167,"lon":7.9500,"address":"Wiesbaden Stop"},{"nodeId":"CGN-DEPOT","lat":50.9333,"lon":6.9600,"address":"Cologne Rhine Depot"}]',
   '["FRA-DEPOT","WIE-STOP","CGN-DEPOT"]',
   NOW() - INTERVAL '45 minutes', NOW() - INTERVAL '45 minutes', NULL),

  -- 5. Stuttgart → Munich (A8)
  ('b1000001-0000-0000-0000-000000000005',
   '3bea21d1-0d38-42c5-97fc-f8073d67aad0', 'f8a11da5-2cd4-441c-96b5-cfa69be50177',
   225.0, 135,  'Active', 'Normal', 'AStar',
   '[{"nodeId":"STU-DEPOT","lat":48.7758,"lon":9.1829,"address":"Stuttgart West Depot"},{"nodeId":"ULM-STOP","lat":48.4011,"lon":9.9876,"address":"Ulm Transfer Hub"},{"nodeId":"MUC-DEPOT","lat":48.1351,"lon":11.5820,"address":"Munich Central Depot"}]',
   '["STU-DEPOT","ULM-STOP","MUC-DEPOT"]',
   NOW() - INTERVAL '3 hours', NOW() - INTERVAL '3 hours', NULL),

  -- 6. Dortmund → Hannover (A2)
  ('b1000001-0000-0000-0000-000000000006',
   '7b6ef527-75a5-4e9b-874f-715811b6b5f4', 'eeeeb28e-861c-40ec-8f09-e1a36bd93f54',
   212.0, 138,  'Active', 'Normal', 'Dijkstra',
   '[{"nodeId":"DOR-DEPOT","lat":51.5136,"lon":7.4653,"address":"Dortmund Industrial Park"},{"nodeId":"BI-STOP","lat":52.0302,"lon":8.5325,"address":"Bielefeld Stop"},{"nodeId":"HAN-DEPOT","lat":52.3759,"lon":9.7320,"address":"Hannover Central Depot"}]',
   '["DOR-DEPOT","BI-STOP","HAN-DEPOT"]',
   NOW() - INTERVAL '1 hour 10 minutes', NOW() - INTERVAL '1 hour 10 minutes', NULL),

  -- 7. Leipzig → Dresden (A14)
  ('b1000001-0000-0000-0000-000000000007',
   '90e2e457-b15b-4c99-81bb-2111d8cd2ca5', '2d6be49c-0a14-41ca-8e93-99b94c0fa4c9',
   114.0,  75,  'Active', 'High', 'AStar',
   '[{"nodeId":"LEI-DEPOT","lat":51.3397,"lon":12.3731,"address":"Leipzig North Depot"},{"nodeId":"DRS-DEPOT","lat":51.0504,"lon":13.7373,"address":"Dresden Elbe Depot"}]',
   '["LEI-DEPOT","DRS-DEPOT"]',
   NOW() - INTERVAL '30 minutes', NOW() - INTERVAL '30 minutes', NULL),

  -- 8. Bremen → Hamburg (A1) return
  ('b1000001-0000-0000-0000-000000000008',
   '9244c42d-7f2e-4857-9f5b-f5025b0e27a8', '07c28f0f-d9bb-4749-b037-241f8b2de8d4',
   123.0,  82,  'Active', 'Normal', 'Dijkstra',
   '[{"nodeId":"BRE-DEPOT","lat":53.0793,"lon":8.8017,"address":"Bremen Logistics Park"},{"nodeId":"HAM-DEPOT","lat":53.5511,"lon":9.9937,"address":"Hamburg Port Depot"}]',
   '["BRE-DEPOT","HAM-DEPOT"]',
   NOW() - INTERVAL '40 minutes', NOW() - INTERVAL '40 minutes', NULL),

  -- 9. Cologne → Dortmund (A1)
  ('b1000001-0000-0000-0000-000000000009',
   '6478481c-f106-4ad0-86ad-354838cb4ca7', 'd7f29d51-5301-4c59-9192-7dca104ceb0d',
   90.0,   62,  'Active', 'Normal', 'AStar',
   '[{"nodeId":"CGN-DEPOT","lat":50.9333,"lon":6.9600,"address":"Cologne Rhine Depot"},{"nodeId":"DOR-DEPOT","lat":51.5136,"lon":7.4653,"address":"Dortmund Industrial Park"}]',
   '["CGN-DEPOT","DOR-DEPOT"]',
   NOW() - INTERVAL '20 minutes', NOW() - INTERVAL '20 minutes', NULL),

  -- 10. Dresden → Berlin (A13) return
  ('b1000001-0000-0000-0000-000000000010',
   '34f3c84f-90fa-46e1-ada8-74bb8cf7f155', 'd7f29d51-5301-4c59-9192-7dca104ceb0d',
   198.0, 120,  'Active', 'High', 'BellmanFord',
   '[{"nodeId":"DRS-DEPOT","lat":51.0504,"lon":13.7373,"address":"Dresden Elbe Depot"},{"nodeId":"LBN-STOP","lat":51.8558,"lon":13.9758,"address":"Lübben Stop"},{"nodeId":"BER-DEPOT","lat":52.5200,"lon":13.4050,"address":"Berlin East Depot"}]',
   '["DRS-DEPOT","LBN-STOP","BER-DEPOT"]',
   NOW() - INTERVAL '1 hour', NOW() - INTERVAL '1 hour', NULL),

  -- 11. Munich → Stuttgart (A8) — Planned
  ('b1000001-0000-0000-0000-000000000011',
   'f2424ec7-a80d-4529-80ba-159652e3f7c7', 'e88f746e-801a-4f1d-a21f-d35aebbdd0b6',
   225.0, 140,  'Planned', 'Normal', 'AStar',
   '[{"nodeId":"MUC-DEPOT","lat":48.1351,"lon":11.5820,"address":"Munich Central Depot"},{"nodeId":"AUG-STOP","lat":48.3705,"lon":10.8978,"address":"Augsburg Stop"},{"nodeId":"STU-DEPOT","lat":48.7758,"lon":9.1829,"address":"Stuttgart West Depot"}]',
   '["MUC-DEPOT","AUG-STOP","STU-DEPOT"]',
   NOW() - INTERVAL '5 minutes', NULL, NULL),

  -- 12. Hannover → Berlin (A2) — Planned
  ('b1000001-0000-0000-0000-000000000012',
   'b013f5a1-de36-43a2-a77d-b668a8160c81', '680edbb4-aa3c-40ed-bcec-45dc7db0be2d',
   285.0, 170,  'Planned', 'High', 'Dijkstra',
   '[{"nodeId":"HAN-DEPOT","lat":52.3759,"lon":9.7320,"address":"Hannover Central Depot"},{"nodeId":"MAG-STOP","lat":52.1205,"lon":11.6276,"address":"Magdeburg Stop"},{"nodeId":"BER-DEPOT","lat":52.5200,"lon":13.4050,"address":"Berlin East Depot"}]',
   '["HAN-DEPOT","MAG-STOP","BER-DEPOT"]',
   NOW() - INTERVAL '10 minutes', NULL, NULL),

  -- 13. Frankfurt → Nuremberg (A3) — Planned
  ('b1000001-0000-0000-0000-000000000013',
   '038ac04e-6cf7-439f-bff1-f7e45363d1ec', '9984a9d2-f1d1-40ec-9668-97f8afe40480',
   222.0, 135,  'Planned', 'Normal', 'AStar',
   '[{"nodeId":"FRA-DEPOT","lat":50.1109,"lon":8.6821,"address":"Frankfurt Airport Logistics"},{"nodeId":"WRZ-STOP","lat":49.7913,"lon":9.9534,"address":"Würzburg Stop"},{"nodeId":"NUE-DEPOT","lat":49.4521,"lon":11.0767,"address":"Nuremberg Distribution Centre"}]',
   '["FRA-DEPOT","WRZ-STOP","NUE-DEPOT"]',
   NOW() - INTERVAL '3 minutes', NULL, NULL),

  -- 14. Stuttgart → Frankfurt (A5) — Planned
  ('b1000001-0000-0000-0000-000000000014',
   '565c34e6-502e-4742-b1df-da2c98bdf13d', '33200fbf-6b02-40a4-bec0-b0bb7b15ef32',
   201.0, 122,  'Planned', 'Normal', 'Dijkstra',
   '[{"nodeId":"STU-DEPOT","lat":48.7758,"lon":9.1829,"address":"Stuttgart West Depot"},{"nodeId":"HHM-STOP","lat":49.3988,"lon":8.6724,"address":"Heidelberg Stop"},{"nodeId":"FRA-DEPOT","lat":50.1109,"lon":8.6821,"address":"Frankfurt Airport Logistics"}]',
   '["STU-DEPOT","HHM-STOP","FRA-DEPOT"]',
   NOW() - INTERVAL '1 minute', NULL, NULL),

  -- 15. Hamburg → Hannover (A7) — Planned
  ('b1000001-0000-0000-0000-000000000015',
   '112c1b54-4bde-4352-b655-dc321472f42e', '0ab6aa67-85d6-4a2f-ac04-f6a300692272',
   152.0,  97,  'Planned', 'Low', 'AStar',
   '[{"nodeId":"HAM-DEPOT","lat":53.5511,"lon":9.9937,"address":"Hamburg Port Depot"},{"nodeId":"HAN-DEPOT","lat":52.3759,"lon":9.7320,"address":"Hannover Central Depot"}]',
   '["HAM-DEPOT","HAN-DEPOT"]',
   NOW() - INTERVAL '2 minutes', NULL, NULL),

  -- 16. Cologne → Frankfurt (A3) — Completed
  ('b1000001-0000-0000-0000-000000000016',
   '5c4216b9-b03c-4d97-85de-99be6a2fd7ba', '0345AB6A-D8E5-46D5-BAB5-0C62694538CC',
   189.0, 116,  'Completed', 'Normal', 'Dijkstra',
   '[{"nodeId":"CGN-DEPOT","lat":50.9333,"lon":6.9600,"address":"Cologne Rhine Depot"},{"nodeId":"LIM-STOP","lat":50.3833,"lon":8.0667,"address":"Limburg Stop"},{"nodeId":"FRA-DEPOT","lat":50.1109,"lon":8.6821,"address":"Frankfurt Airport Logistics"}]',
   '["CGN-DEPOT","LIM-STOP","FRA-DEPOT"]',
   NOW() - INTERVAL '6 hours', NOW() - INTERVAL '6 hours', NOW() - INTERVAL '4 hours'),

  -- 17. Nuremberg → Munich (A9) — Completed
  ('b1000001-0000-0000-0000-000000000017',
   'f2424ec7-a80d-4529-80ba-159652e3f7c7', 'e88f746e-801a-4f1d-a21f-d35aebbdd0b6',
   171.0, 105,  'Completed', 'High', 'AStar',
   '[{"nodeId":"NUE-DEPOT","lat":49.4521,"lon":11.0767,"address":"Nuremberg Distribution Centre"},{"nodeId":"ING-HUB","lat":48.7665,"lon":11.4258,"address":"Ingolstadt Logistics Hub"},{"nodeId":"MUC-DEPOT","lat":48.1351,"lon":11.5820,"address":"Munich Central Depot"}]',
   '["NUE-DEPOT","ING-HUB","MUC-DEPOT"]',
   NOW() - INTERVAL '8 hours', NOW() - INTERVAL '8 hours', NOW() - INTERVAL '6 hours'),

  -- 18. Berlin → Hamburg (A24) — Completed
  ('b1000001-0000-0000-0000-000000000018',
   '12aedea1-7e13-4e54-9c9a-622e0194b448', 'dabe68fd-66dd-4835-9264-ae99bdecd1f5',
   289.0, 175,  'Completed', 'Normal', 'BellmanFord',
   '[{"nodeId":"BER-DEPOT","lat":52.5200,"lon":13.4050,"address":"Berlin East Depot"},{"nodeId":"WIT-STOP","lat":53.0333,"lon":11.7500,"address":"Wittenberge Stop"},{"nodeId":"HAM-DEPOT","lat":53.5511,"lon":9.9937,"address":"Hamburg Port Depot"}]',
   '["BER-DEPOT","WIT-STOP","HAM-DEPOT"]',
   NOW() - INTERVAL '10 hours', NOW() - INTERVAL '10 hours', NOW() - INTERVAL '7 hours'),

  -- 19. Frankfurt → Dortmund (A45) — Cancelled
  ('b1000001-0000-0000-0000-000000000019',
   '038ac04e-6cf7-439f-bff1-f7e45363d1ec', NULL,
   237.0, 148,  'Cancelled', 'Low', 'Dijkstra',
   '[{"nodeId":"FRA-DEPOT","lat":50.1109,"lon":8.6821,"address":"Frankfurt Airport Logistics"},{"nodeId":"SIE-STOP","lat":50.8757,"lon":8.0243,"address":"Siegen Stop"},{"nodeId":"DOR-DEPOT","lat":51.5136,"lon":7.4653,"address":"Dortmund Industrial Park"}]',
   '["FRA-DEPOT","SIE-STOP","DOR-DEPOT"]',
   NOW() - INTERVAL '4 hours', NULL, NULL),

  -- 20. Munich → Berlin (A9) long haul — Completed
  ('b1000001-0000-0000-0000-000000000020',
   '34f3c84f-90fa-46e1-ada8-74bb8cf7f155', 'd7f29d51-5301-4c59-9192-7dca104ceb0d',
   584.0, 350,  'Completed', 'Emergency', 'AStar',
   '[{"nodeId":"MUC-DEPOT","lat":48.1351,"lon":11.5820,"address":"Munich Central Depot"},{"nodeId":"NUE-DEPOT","lat":49.4521,"lon":11.0767,"address":"Nuremberg Distribution Centre"},{"nodeId":"HOF-STOP","lat":50.3133,"lon":11.9128,"address":"Hof Stop"},{"nodeId":"LEI-DEPOT","lat":51.3397,"lon":12.3731,"address":"Leipzig North Depot"},{"nodeId":"BER-DEPOT","lat":52.5200,"lon":13.4050,"address":"Berlin East Depot"}]',
   '["MUC-DEPOT","NUE-DEPOT","HOF-STOP","LEI-DEPOT","BER-DEPOT"]',
   NOW() - INTERVAL '14 hours', NOW() - INTERVAL '14 hours', NOW() - INTERVAL '8 hours');
