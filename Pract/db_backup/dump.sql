--
-- PostgreSQL database dump
--

\restrict 0KLv8DuEYWnddpCYxdknQXvA61u61J2dINqCzzC5s0shB1QQFoMFd6KrOrgsfll

-- Dumped from database version 18.4
-- Dumped by pg_dump version 18.4

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: signal_track_db; Type: DATABASE; Schema: -; Owner: -
--

CREATE DATABASE signal_track_db WITH TEMPLATE = template0 ENCODING = 'UTF8' LOCALE_PROVIDER = libc LOCALE = 'Russian_Russia.1251';


\unrestrict 0KLv8DuEYWnddpCYxdknQXvA61u61J2dINqCzzC5s0shB1QQFoMFd6KrOrgsfll
\connect signal_track_db
\restrict 0KLv8DuEYWnddpCYxdknQXvA61u61J2dINqCzzC5s0shB1QQFoMFd6KrOrgsfll

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: check_reading_status(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.check_reading_status() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_min NUMERIC;
    v_max NUMERIC;
    v_numeric_value NUMERIC;
    v_ok_id INT;
    v_warning_id INT;
    v_error_id INT;
BEGIN
    SELECT status_id INTO v_ok_id FROM "status" WHERE "name" ILIKE 'OK' LIMIT 1;
    SELECT status_id INTO v_warning_id FROM "status" WHERE "name" ILIKE 'WARNING' LIMIT 1;
    SELECT status_id INTO v_error_id FROM "status" WHERE "name" ILIKE 'ERROR' LIMIT 1;

    SELECT min_value, max_value 
    INTO v_min, v_max 
    FROM "reading_type" 
    WHERE "reading_type_id" = NEW.reading_type_id;

    IF v_min IS NULL AND v_max IS NULL THEN
        NEW.status_id := v_ok_id;
        RETURN NEW;
    END IF;

    BEGIN
        v_numeric_value := CAST(NEW.value AS NUMERIC);
    EXCEPTION WHEN others THEN
        v_numeric_value := NULL;
    END;

    IF v_numeric_value IS NULL THEN
        NEW.status_id := v_error_id;
        RETURN NEW;
    END IF;

    IF v_min IS NOT NULL AND v_numeric_value < v_min THEN
        NEW.status_id := v_error_id;
    ELSIF v_max IS NOT NULL AND v_numeric_value > v_max THEN
        NEW.status_id := v_warning_id;
    ELSE
        NEW.status_id := v_ok_id;
    END IF;

    RETURN NEW;
END;
$$;


--
-- Name: clean_expired_sessions(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.clean_expired_sessions() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    DELETE FROM "session" 
    WHERE "user_id" = NEW."user_id" 
      AND "session_end_date_time" < NOW();

    RETURN NEW;
END;
$$;


--
-- Name: clean_old_reading_logs(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.clean_old_reading_logs() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    DELETE FROM reading_log 
    WHERE write_date_time < NOW() - INTERVAL '1 year';
    
    RETURN NEW;
END;
$$;


--
-- Name: create_new_user(character varying, character varying, character varying, character varying, character varying, character varying); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.create_new_user(IN p_login character varying, IN p_hashed_password character varying, IN p_first_name character varying, IN p_last_name character varying, IN p_second_name character varying, IN p_role_name character varying)
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_user_id INT;
    v_role_id INT;
BEGIN
    SELECT role_id INTO v_role_id FROM role WHERE name = p_role_name;
    
    IF v_role_id IS NULL THEN
        RAISE EXCEPTION 'Роль % не найдена', p_role_name;
    END IF;

    INSERT INTO "user" (login, hashed_password, first_name, last_name, second_name)
    VALUES (p_login, p_hashed_password, p_first_name, p_last_name, p_second_name)
    RETURNING user_id INTO v_user_id;

    INSERT INTO user_role (user_id, role_id)
    VALUES (v_user_id, v_role_id);

    RAISE NOTICE 'Пользователь % успешно создан с ролью %', p_login, p_role_name;

EXCEPTION
    WHEN unique_violation THEN
        RAISE EXCEPTION 'Пользователь с логином % уже существует', p_login;
    WHEN OTHERS THEN
        RAISE;
END;
$$;


--
-- Name: fn_create_alert_on_equipment_status(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_create_alert_on_equipment_status() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_status_name TEXT;
    v_severity VARCHAR(20);
    v_msg VARCHAR(255);
    v_alert_id INT;
BEGIN
    IF NEW.status_id IS DISTINCT FROM OLD.status_id THEN
        SELECT LOWER(name) INTO v_status_name FROM public.status WHERE status_id = NEW.status_id;

        IF v_status_name IS NOT NULL AND v_status_name <> 'ok' THEN
            v_severity := CASE WHEN v_status_name = 'error' THEN 'CRITICAL' ELSE 'WARNING' END;
            v_msg := LEFT(format('Прибор %s (%s) перешёл в статус: %s', NEW.name, NEW.serial_number, v_status_name), 255);

            INSERT INTO public.alert (equipment_id, reading_log_id, severity, message, is_resolved, created_at)
            VALUES (NEW.equipment_id, NULL, v_severity, v_msg, false, NOW())
            RETURNING alert_id INTO v_alert_id;

            INSERT INTO public.user_alert (alert_id, user_id, is_read)
            SELECT v_alert_id, u.user_id, false
              FROM public."user" u
            ON CONFLICT (alert_id, user_id) DO NOTHING;
        END IF;
    END IF;

    RETURN NEW;
END;
$$;


--
-- Name: fn_create_alert_on_reading_log(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_create_alert_on_reading_log() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_status_name TEXT;
    v_eq_name TEXT;
    v_eq_serial TEXT;
    v_rt_name TEXT;
    v_rt_unit TEXT;
    v_severity VARCHAR(20);
    v_msg VARCHAR(255);
    v_alert_id INT;
BEGIN
    SELECT LOWER(name) INTO v_status_name FROM public.status WHERE status_id = NEW.status_id;

    IF v_status_name IS NOT NULL AND v_status_name <> 'ok' THEN
        SELECT name, serial_number INTO v_eq_name, v_eq_serial
          FROM public.equipment WHERE equipment_id = NEW.equipment_id;

        SELECT name, COALESCE(unit, '') INTO v_rt_name, v_rt_unit
          FROM public.reading_type WHERE reading_type_id = NEW.reading_type_id;

        v_severity := CASE WHEN v_status_name = 'error' THEN 'CRITICAL' ELSE 'WARNING' END;
        v_msg := LEFT(format('Прибор %s (%s): %s = %s %s (статус: %s)',
                      COALESCE(v_eq_name, '—'), COALESCE(v_eq_serial, '—'),
                      COALESCE(v_rt_name, 'параметр'), NEW.value, v_rt_unit, v_status_name), 255);

        INSERT INTO public.alert (equipment_id, reading_log_id, severity, message, is_resolved, created_at)
        VALUES (NEW.equipment_id, NEW.reading_log_id, v_severity, v_msg, false, COALESCE(NEW.write_date_time, NOW()))
        RETURNING alert_id INTO v_alert_id;

        INSERT INTO public.user_alert (alert_id, user_id, is_read)
        SELECT v_alert_id, u.user_id, false
          FROM public."user" u
        ON CONFLICT (alert_id, user_id) DO NOTHING;
    END IF;

    RETURN NEW;
END;
$$;


--
-- Name: get_equipment_logs(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.get_equipment_logs(p_id integer) RETURNS TABLE(reading_log_id integer, write_date_time timestamp with time zone, value character varying, status_id integer)
    LANGUAGE sql
    AS $$ 
    SELECT "reading_log_id", "write_date_time", "value", "status_id" 
    FROM "reading_log" 
    WHERE "equipment_id" = p_id;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: admin_audit_log; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.admin_audit_log (
    audit_log_id integer NOT NULL,
    user_id integer,
    action character varying(100) NOT NULL,
    entity_name character varying(50) NOT NULL,
    entity_id integer,
    details jsonb,
    ip_address character varying(45),
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: admin_audit_log_audit_log_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.admin_audit_log_audit_log_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: admin_audit_log_audit_log_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.admin_audit_log_audit_log_id_seq OWNED BY public.admin_audit_log.audit_log_id;


--
-- Name: alert; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.alert (
    alert_id integer NOT NULL,
    equipment_id integer NOT NULL,
    reading_log_id integer,
    severity character varying(20) DEFAULT 'WARNING'::character varying NOT NULL,
    message character varying(255) NOT NULL,
    is_resolved boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    resolved_at timestamp with time zone,
    CONSTRAINT chk_alert_severity CHECK (((severity)::text = ANY ((ARRAY['INFO'::character varying, 'WARNING'::character varying, 'CRITICAL'::character varying, 'FATAL'::character varying])::text[])))
);


--
-- Name: alert_alert_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.alert_alert_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: alert_alert_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.alert_alert_id_seq OWNED BY public.alert.alert_id;


--
-- Name: equipment; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.equipment (
    equipment_id integer NOT NULL,
    status_id integer NOT NULL,
    serial_number character varying(100) NOT NULL,
    name character varying(150) NOT NULL,
    model character varying(100) NOT NULL,
    ip_address character varying(39),
    is_deleted boolean DEFAULT false NOT NULL,
    poll_enabled boolean DEFAULT true NOT NULL
);


--
-- Name: equipment_command; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.equipment_command (
    command_id integer NOT NULL,
    equipment_id integer NOT NULL,
    reading_type_id integer NOT NULL,
    command_string character varying(50) NOT NULL,
    invalid_value character varying(50),
    poll_interval_seconds integer DEFAULT 30 NOT NULL
);


--
-- Name: equipment_command_command_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.equipment_command_command_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: equipment_command_command_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.equipment_command_command_id_seq OWNED BY public.equipment_command.command_id;


--
-- Name: equipment_equipment_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.equipment_equipment_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: equipment_equipment_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.equipment_equipment_id_seq OWNED BY public.equipment.equipment_id;


--
-- Name: reading_log; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reading_log (
    reading_log_id integer NOT NULL,
    equipment_id integer NOT NULL,
    status_id integer NOT NULL,
    reading_type_id integer NOT NULL,
    write_date_time timestamp with time zone DEFAULT now() NOT NULL,
    value character varying(50) NOT NULL
);


--
-- Name: reading_log_reading_log_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.reading_log_reading_log_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: reading_log_reading_log_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.reading_log_reading_log_id_seq OWNED BY public.reading_log.reading_log_id;


--
-- Name: reading_type; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reading_type (
    reading_type_id integer NOT NULL,
    identifier character varying(19) NOT NULL,
    name character varying(50) NOT NULL,
    unit character varying(20),
    min_value numeric(10,2),
    max_value numeric(10,2)
);


--
-- Name: reading_type_reading_type_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.reading_type_reading_type_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: reading_type_reading_type_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.reading_type_reading_type_id_seq OWNED BY public.reading_type.reading_type_id;


--
-- Name: role; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.role (
    role_id integer NOT NULL,
    name character varying(50) NOT NULL
);


--
-- Name: role_role_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.role_role_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: role_role_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.role_role_id_seq OWNED BY public.role.role_id;


--
-- Name: session; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.session (
    session_id integer NOT NULL,
    user_id integer NOT NULL,
    refresh_token text NOT NULL,
    device character varying(255),
    session_end_date_time timestamp with time zone NOT NULL
);


--
-- Name: session_session_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.session_session_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: session_session_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.session_session_id_seq OWNED BY public.session.session_id;


--
-- Name: status; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.status (
    status_id integer NOT NULL,
    name character varying(50) NOT NULL
);


--
-- Name: status_status_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.status_status_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: status_status_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.status_status_id_seq OWNED BY public.status.status_id;


--
-- Name: user; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."user" (
    user_id integer NOT NULL,
    login character varying(50) NOT NULL,
    hashed_password character varying(255) NOT NULL,
    second_name character varying(100) NOT NULL,
    first_name character varying(100) NOT NULL,
    last_name character varying(100)
);


--
-- Name: user_alert; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_alert (
    user_alert_id integer NOT NULL,
    alert_id integer NOT NULL,
    user_id integer NOT NULL,
    is_read boolean DEFAULT false NOT NULL,
    read_at timestamp with time zone
);


--
-- Name: user_alert_user_alert_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.user_alert_user_alert_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: user_alert_user_alert_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.user_alert_user_alert_id_seq OWNED BY public.user_alert.user_alert_id;


--
-- Name: user_role; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_role (
    user_id integer NOT NULL,
    role_id integer NOT NULL
);


--
-- Name: user_user_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.user_user_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: user_user_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.user_user_id_seq OWNED BY public."user".user_id;


--
-- Name: view_latest_equipment_readings; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.view_latest_equipment_readings AS
 WITH ranked_readings AS (
         SELECT rl.equipment_id,
            rl.reading_type_id,
            rl.write_date_time,
            rl.value,
            rl.status_id,
            row_number() OVER (PARTITION BY rl.equipment_id, rl.reading_type_id ORDER BY rl.write_date_time DESC) AS rn
           FROM public.reading_log rl
        )
 SELECT eq.serial_number,
    rt.identifier AS metric_key,
    rt.name AS label,
    rr.value,
    rt.unit,
    st.name AS status,
    rr.write_date_time AS datetime,
    rt.min_value,
    rt.max_value
   FROM (((ranked_readings rr
     JOIN public.equipment eq ON ((rr.equipment_id = eq.equipment_id)))
     JOIN public.reading_type rt ON ((rr.reading_type_id = rt.reading_type_id)))
     JOIN public.status st ON ((rr.status_id = st.status_id)))
  WHERE (rr.rn = 1);


--
-- Name: view_new_equipment; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.view_new_equipment AS
 SELECT equipment_id,
    status_id,
    serial_number,
    name,
    model,
    ip_address
   FROM public.equipment
  WHERE ((name)::text = 'New Equipment'::text);


--
-- Name: admin_audit_log audit_log_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_audit_log ALTER COLUMN audit_log_id SET DEFAULT nextval('public.admin_audit_log_audit_log_id_seq'::regclass);


--
-- Name: alert alert_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.alert ALTER COLUMN alert_id SET DEFAULT nextval('public.alert_alert_id_seq'::regclass);


--
-- Name: equipment equipment_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.equipment ALTER COLUMN equipment_id SET DEFAULT nextval('public.equipment_equipment_id_seq'::regclass);


--
-- Name: equipment_command command_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.equipment_command ALTER COLUMN command_id SET DEFAULT nextval('public.equipment_command_command_id_seq'::regclass);


--
-- Name: reading_log reading_log_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reading_log ALTER COLUMN reading_log_id SET DEFAULT nextval('public.reading_log_reading_log_id_seq'::regclass);


--
-- Name: reading_type reading_type_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reading_type ALTER COLUMN reading_type_id SET DEFAULT nextval('public.reading_type_reading_type_id_seq'::regclass);


--
-- Name: role role_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role ALTER COLUMN role_id SET DEFAULT nextval('public.role_role_id_seq'::regclass);


--
-- Name: session session_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.session ALTER COLUMN session_id SET DEFAULT nextval('public.session_session_id_seq'::regclass);


--
-- Name: status status_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.status ALTER COLUMN status_id SET DEFAULT nextval('public.status_status_id_seq'::regclass);


--
-- Name: user user_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."user" ALTER COLUMN user_id SET DEFAULT nextval('public.user_user_id_seq'::regclass);


--
-- Name: user_alert user_alert_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_alert ALTER COLUMN user_alert_id SET DEFAULT nextval('public.user_alert_user_alert_id_seq'::regclass);


--
-- Data for Name: admin_audit_log; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.admin_audit_log VALUES (1, 4, 'CREATE_USER', 'user', NULL, '{"Role": null, "Login": "test", "FirstName": "test", "SecondName": "test"}', '127.0.0.1', '2026-11-08 17:06:56.841581+03');
INSERT INTO public.admin_audit_log VALUES (2, 4, 'UPDATE_USER_ROLES', 'user_role', 27, '{"roles": ["Инженер", "Администратор"], "targetUserId": 27}', '127.0.0.1', '2026-11-08 17:09:45.311695+03');
INSERT INTO public.admin_audit_log VALUES (3, 4, 'DELETE_USER', 'user', 27, '{"deletedUserId": 27}', '127.0.0.1', '2026-11-09 14:32:38.706352+03');
INSERT INTO public.admin_audit_log VALUES (4, 4, 'CREATE_USER', 'user', 28, '{"login": "tets", "roles": ["Инженер"], "lastName": "test", "firstName": "test", "secondName": "test"}', '127.0.0.1', '2026-11-09 15:09:59.67869+03');
INSERT INTO public.admin_audit_log VALUES (5, 4, 'UPDATE_USER_ROLES', 'user_role', 28, '{"roles": ["Инженер", "Администратор"], "targetUserId": 28}', '127.0.0.1', '2026-11-09 15:11:01.364238+03');
INSERT INTO public.admin_audit_log VALUES (6, 4, 'UPDATE_USER_ROLES', 'user_role', 28, '{"roles": ["Инженер"], "targetUserId": 28}', '127.0.0.1', '2026-11-09 15:11:03.837294+03');
INSERT INTO public.admin_audit_log VALUES (7, 4, 'UPDATE_USER_ROLES', 'user_role', 28, '{"roles": ["Инженер", "Администратор"], "targetUserId": 28}', '127.0.0.1', '2026-11-09 15:11:06.1235+03');
INSERT INTO public.admin_audit_log VALUES (8, 4, 'UPDATE_USER_ROLES', 'user_role', 28, '{"roles": ["Инженер", "Администратор"], "targetUserId": 28}', '127.0.0.1', '2026-11-09 15:11:09.311726+03');
INSERT INTO public.admin_audit_log VALUES (9, 4, 'RESET_PASSWORD', 'user', 28, '{"targetUserId": 28}', '127.0.0.1', '2026-11-09 15:11:09.556366+03');
INSERT INTO public.admin_audit_log VALUES (10, 4, 'UPDATE_USER_ROLES', 'user_role', 28, '{"roles": ["Инженер", "Администратор"], "targetUserId": 28}', '127.0.0.1', '2026-11-09 15:11:14.218952+03');
INSERT INTO public.admin_audit_log VALUES (11, 4, 'RESET_PASSWORD', 'user', 28, '{"targetUserId": 28}', '127.0.0.1', '2026-11-09 15:11:14.409549+03');
INSERT INTO public.admin_audit_log VALUES (12, 4, 'UPDATE_USER_ROLES', 'user_role', 28, '{"roles": ["Инженер"], "targetUserId": 28}', '127.0.0.1', '2026-11-09 15:11:16.97669+03');
INSERT INTO public.admin_audit_log VALUES (13, 4, 'UPDATE_USER', 'user', 28, '{"login": "tets", "lastName": "test", "firstName": "123", "secondName": "test"}', '127.0.0.1', '2026-11-09 15:11:20.452476+03');
INSERT INTO public.admin_audit_log VALUES (14, 4, 'UPDATE_USER', 'user', 28, '{"login": "tets", "lastName": "test", "firstName": "123", "secondName": "123"}', '127.0.0.1', '2026-11-09 15:11:22.934064+03');
INSERT INTO public.admin_audit_log VALUES (15, 4, 'UPDATE_USER', 'user', 28, '{"login": "tets", "lastName": "123", "firstName": "123", "secondName": "123"}', '127.0.0.1', '2026-11-09 15:11:27.416028+03');
INSERT INTO public.admin_audit_log VALUES (16, 4, 'UPDATE_USER', 'user', 28, '{"login": "123", "lastName": "123", "firstName": "123", "secondName": "123"}', '127.0.0.1', '2026-11-09 15:11:30.533656+03');
INSERT INTO public.admin_audit_log VALUES (17, 4, 'IMPORT_AGENT_CONFIG', 'equipment_command', NULL, '{"devicesCount": 2, "updatedDevices": 2}', '127.0.0.1', '2026-11-17 18:57:32.007634+03');
INSERT INTO public.admin_audit_log VALUES (18, 4, 'IMPORT_AGENT_CONFIG', 'equipment_command', NULL, '{"devicesCount": 7, "updatedDevices": 7}', '127.0.0.1', '2026-11-17 18:59:12.484902+03');
INSERT INTO public.admin_audit_log VALUES (19, 4, 'IMPORT_AGENT_CONFIG', 'equipment_command', NULL, '{"devicesCount": 7, "updatedDevices": 7}', '127.0.0.1', '2026-11-17 18:59:53.726638+03');
INSERT INTO public.admin_audit_log VALUES (20, 4, 'UPDATE_USER', 'user', 28, '{"login": "https", "lastName": "123", "firstName": "123", "secondName": "123"}', '10.175.173.134', '2026-11-17 19:29:24.360605+03');
INSERT INTO public.admin_audit_log VALUES (21, 4, 'IMPORT_AGENT_CONFIG', 'equipment_command', NULL, '{"devicesCount": 5, "updatedDevices": 5}', '10.175.173.134', '2026-11-17 19:41:04.78258+03');
INSERT INTO public.admin_audit_log VALUES (22, 4, 'IMPORT_AGENT_CONFIG', 'equipment_command', NULL, '{"devicesCount": 6, "updatedDevices": 6}', '10.175.173.134', '2026-11-17 19:41:10.794457+03');
INSERT INTO public.admin_audit_log VALUES (23, 4, 'IMPORT_AGENT_CONFIG', 'equipment_command', NULL, '{"devicesCount": 5, "updatedDevices": 5}', '10.175.173.134', '2026-11-17 19:43:55.137128+03');
INSERT INTO public.admin_audit_log VALUES (24, 4, 'IMPORT_AGENT_CONFIG', 'equipment_command', NULL, '{"devicesCount": 6, "updatedDevices": 6}', '10.175.173.134', '2026-11-17 19:46:03.654473+03');
INSERT INTO public.admin_audit_log VALUES (25, 4, 'UPDATE_EQUIPMENT', 'equipment', 20, '{"name": "Device 0.0.0.1", "model": "Other", "ipAddress": null, "isDeleted": false, "statusName": "ok", "equipmentId": 20, "pollEnabled": true, "serialNumber": "sernumex123"}', '10.175.173.134', '2026-11-17 19:47:13.765778+03');
INSERT INTO public.admin_audit_log VALUES (26, 4, 'DELETE_EQUIPMENT', 'equipment', 18, '{"equipmentId": 18, "softDeleted": true}', '10.175.173.134', '2026-11-17 19:54:11.867727+03');
INSERT INTO public.admin_audit_log VALUES (27, 4, 'IMPORT_AGENT_CONFIG', 'equipment_command', NULL, '{"devicesCount": 8, "updatedDevices": 8}', '10.175.173.134', '2026-11-17 20:05:37.595371+03');
INSERT INTO public.admin_audit_log VALUES (28, 4, 'CREATE_EQUIPMENT', 'equipment', 21, '{"name": "dobavleno", "model": "1231233132", "ipAddress": "12312312312", "isDeleted": false, "statusName": "Ok", "equipmentId": 21, "pollEnabled": true, "serialNumber": "12312313"}', '10.175.173.134', '2026-11-17 20:29:45.850732+03');
INSERT INTO public.admin_audit_log VALUES (29, 4, 'UPDATE_EQUIPMENT', 'equipment', 21, '{"name": "dobavleno1", "model": "1231233132", "ipAddress": "12312312312", "isDeleted": false, "statusName": "ok", "equipmentId": 21, "pollEnabled": true, "serialNumber": "12312313"}', '10.175.173.134', '2026-11-17 20:30:01.882195+03');
INSERT INTO public.admin_audit_log VALUES (30, 4, 'IMPORT_AGENT_CONFIG', 'equipment_command', NULL, '{"devicesCount": 8, "updatedDevices": 8}', '10.175.173.134', '2026-11-17 20:30:28.711919+03');
INSERT INTO public.admin_audit_log VALUES (31, 4, 'CREATE_EQUIPMENT', 'equipment', 22, '{"name": "test1", "model": "test", "ipAddress": "127.0.0.1", "isDeleted": false, "statusName": "Ok", "equipmentId": 22, "pollEnabled": true, "serialNumber": "oiaijgarijgpa;"}', '10.175.173.134', '2026-11-23 13:22:12.743957+03');
INSERT INTO public.admin_audit_log VALUES (32, 4, 'DELETE_EQUIPMENT', 'equipment', 22, '{"equipmentId": 22, "softDeleted": true}', '10.175.173.134', '2026-11-23 13:23:12.638852+03');
INSERT INTO public.admin_audit_log VALUES (33, 4, 'IMPORT_AGENT_CONFIG', 'equipment_command', NULL, '{"devicesCount": 8, "updatedDevices": 8}', '10.175.173.134', '2026-11-23 13:24:23.919619+03');


--
-- Data for Name: alert; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.alert VALUES (1, 11, 309923, 'WARNING', 'Прибор Мегафон Н3 (SerNumEx): Температура = 35 °C (статус: warning)', true, '2026-11-10 17:52:57.267233+03', '2026-11-12 21:20:27.162427+03');
INSERT INTO public.alert VALUES (2, 11, 309922, 'WARNING', 'Прибор Мегафон Н3 (SerNumEx): параметр ''Температура'' = 34 °C (статус: warning).', true, '2026-11-09 17:40:46.92337+03', '2026-11-12 21:20:27.162427+03');
INSERT INTO public.alert VALUES (3, 11, 309732, 'CRITICAL', 'Прибор Мегафон Н3 (SerNumEx): параметр ''Температура'' = +30 °C (статус: error).', true, '2026-06-11 09:56:40.039996+03', '2026-11-12 21:20:27.162428+03');
INSERT INTO public.alert VALUES (4, 11, 309726, 'CRITICAL', 'Прибор Мегафон Н3 (SerNumEx): параметр ''Температура'' = +31 °C (статус: error).', true, '2026-06-11 09:56:29.234088+03', '2026-11-12 21:20:27.162428+03');
INSERT INTO public.alert VALUES (5, 11, 309720, 'WARNING', 'Прибор Мегафон Н3 (SerNumEx): параметр ''Температура'' = +31 °C (статус: warning).', true, '2026-06-11 09:56:18.364772+03', '2026-11-12 21:20:27.162428+03');
INSERT INTO public.alert VALUES (6, 11, 309714, 'WARNING', 'Прибор Мегафон Н3 (SerNumEx): параметр ''Температура'' = +30 °C (статус: warning).', true, '2026-06-11 09:56:07.545259+03', '2026-11-12 21:20:27.162428+03');
INSERT INTO public.alert VALUES (7, 11, 309708, 'WARNING', 'Прибор Мегафон Н3 (SerNumEx): параметр ''Температура'' = +30 °C (статус: warning).', true, '2026-06-11 09:55:56.67794+03', '2026-11-12 21:20:27.162428+03');
INSERT INTO public.alert VALUES (8, 11, 309924, 'WARNING', 'Прибор Мегафон Н3 (SerNumEx): Температура = 36 °C (статус: warning)', true, '2026-11-10 18:15:37.659357+03', '2026-11-12 21:20:27.162351+03');
INSERT INTO public.alert VALUES (15, 12, 309931, 'WARNING', 'Ex Eq: вне нормы Температура (37 °C)', true, '2026-11-12 21:22:54.079815+03', '2026-11-12 22:11:35.899099+03');
INSERT INTO public.alert VALUES (9, 11, 309925, 'WARNING', 'Мегафон Н3: вне нормы Температура (36 °C)', true, '2026-11-12 21:20:27.287562+03', '2026-11-12 21:22:54.067883+03');
INSERT INTO public.alert VALUES (10, 11, 309926, 'CRITICAL', 'Прибор Мегафон Н3 (SerNumEx): rsl = -1  (статус: error)', true, '2026-11-12 21:21:48.608118+03', '2026-11-12 21:22:54.067883+03');
INSERT INTO public.alert VALUES (11, 11, 309927, 'CRITICAL', 'Прибор Мегафон Н3 (SerNumEx): rsl = -1  (статус: error)', true, '2026-11-12 21:21:53.249013+03', '2026-11-12 21:22:54.067883+03');
INSERT INTO public.alert VALUES (12, 11, 309928, 'WARNING', 'Прибор Мегафон Н3 (SerNumEx): Температура = 37 °C (статус: warning)', true, '2026-11-12 21:22:04.38192+03', '2026-11-12 21:22:54.067883+03');
INSERT INTO public.alert VALUES (13, 11, 309929, 'WARNING', 'Прибор Мегафон Н3 (SerNumEx): Температура = 37 °C (статус: warning)', true, '2026-11-12 21:22:07.877406+03', '2026-11-12 21:22:54.067882+03');
INSERT INTO public.alert VALUES (18, 12, 309935, 'WARNING', 'Ex Eq: вне нормы Температура (+35 °C)', false, '2026-11-12 22:11:35.902074+03', NULL);
INSERT INTO public.alert VALUES (14, 11, 309930, 'CRITICAL', 'Мегафон Н3: вне нормы Температура (37 °C), rsl (-1)', true, '2026-11-12 21:22:54.075896+03', '2026-11-12 21:54:55.727597+03');
INSERT INTO public.alert VALUES (19, 13, 309936, 'WARNING', 'Ex Eq1: вне нормы Температура (+35 °C)', false, '2026-11-12 22:11:35.904155+03', NULL);
INSERT INTO public.alert VALUES (20, 14, 309937, 'WARNING', 'Ex Eq2: вне нормы Температура (+35 °C)', false, '2026-11-12 22:11:35.906508+03', NULL);
INSERT INTO public.alert VALUES (22, 16, 309939, 'WARNING', 'Прибор Ex Eq3 (SerNumEx6): Температура = +35 °C (статус: warning)', false, '2026-11-12 22:11:06.720834+03', NULL);
INSERT INTO public.alert VALUES (23, 17, 309940, 'WARNING', 'Прибор Ex Eq4 (SerNumEx7): Температура = +35 °C (статус: warning)', false, '2026-11-12 22:11:12.601673+03', NULL);
INSERT INTO public.alert VALUES (16, 11, 309932, 'CRITICAL', 'Мегафон Н3: вне нормы rsl (-1)', true, '2026-11-12 21:55:38.392478+03', '2026-11-12 22:11:35.884704+03');
INSERT INTO public.alert VALUES (17, 11, 309934, 'WARNING', 'Прибор Мегафон Н3 (SerNumEx): Температура = +35 °C (статус: warning)', true, '2026-11-12 22:10:37.323391+03', '2026-11-12 22:11:35.884703+03');
INSERT INTO public.alert VALUES (25, 11, 309943, 'CRITICAL', 'Мегафон Н3: вне нормы Температура (+35 °C), rsl (-1)', false, '2026-11-12 22:11:35.89676+03', NULL);
INSERT INTO public.alert VALUES (21, 15, 309938, 'WARNING', 'Ex Eq3: вне нормы Температура (+35 °C)', false, '2026-11-12 22:11:35.908384+03', NULL);
INSERT INTO public.alert VALUES (24, 18, 309941, 'WARNING', 'Ex Eq5: вне нормы Температура (+35 °C)', false, '2026-11-12 22:11:35.910152+03', NULL);


--
-- Data for Name: equipment; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.equipment VALUES (11, 1, 'SerNumEx', 'Мегафон Н3', 'Other', '192.168.100.192', false, true);
INSERT INTO public.equipment VALUES (12, 1, 'SerNumEx2', 'Ex Eq', 'new model', 'Some IP', false, true);
INSERT INTO public.equipment VALUES (13, 1, 'SerNumEx3', 'Ex Eq1', 'new model', 'Some IP', false, true);
INSERT INTO public.equipment VALUES (14, 1, 'SerNumEx4', 'Ex Eq2', 'new model', 'Some IP', false, true);
INSERT INTO public.equipment VALUES (15, 1, 'SerNumEx5', 'Ex Eq3', 'new model', 'Some IP', false, true);
INSERT INTO public.equipment VALUES (16, 1, 'SerNumEx6', 'Ex Eq3', 'new model', 'Some IP', true, true);
INSERT INTO public.equipment VALUES (17, 1, 'SerNumEx7', 'Ex Eq4', 'new model', 'Some IP', true, true);
INSERT INTO public.equipment VALUES (19, 1, '9aa62da1-a22a-4dbc-84f8-b3df598572e1', 'Device 0.0.0.0', 'Other', '0.0.0.0', false, true);
INSERT INTO public.equipment VALUES (20, 1, 'sernumex123', 'Device 0.0.0.1', 'Other', '0.0.0.1', false, true);
INSERT INTO public.equipment VALUES (18, 1, 'SerNumEx8', 'Ex Eq5', 'new model', 'Some IP', true, false);
INSERT INTO public.equipment VALUES (21, 1, '12312313', 'dobavleno1', '1231233132', '12312312312', false, true);
INSERT INTO public.equipment VALUES (22, 1, 'oiaijgarijgpa;', 'test1', 'test', '127.0.0.1', true, false);


--
-- Data for Name: equipment_command; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.equipment_command VALUES (1, 11, 29, '<0000/ber?', '99999', 30);
INSERT INTO public.equipment_command VALUES (2, 11, 30, '<0000/EBN?', '99.9', 30);
INSERT INTO public.equipment_command VALUES (3, 11, 31, '<0000/TMP?', NULL, 30);
INSERT INTO public.equipment_command VALUES (4, 11, 32, '<0000/RSL?', NULL, 30);
INSERT INTO public.equipment_command VALUES (5, 11, 33, '<0000/TXO?', NULL, 30);
INSERT INTO public.equipment_command VALUES (6, 11, 34, '<0000/TTC?', NULL, 30);
INSERT INTO public.equipment_command VALUES (7, 12, 35, 'cmd?', NULL, 30);
INSERT INTO public.equipment_command VALUES (8, 21, 35, 'cmd?', NULL, 30);


--
-- Data for Name: reading_log; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.reading_log VALUES (309574, 11, 1, 29, '2026-06-11 09:51:58.698148+03', 'n/a');
INSERT INTO public.reading_log VALUES (309575, 11, 1, 30, '2026-06-11 09:51:58.90394+03', 'n/a');
INSERT INTO public.reading_log VALUES (309576, 11, 1, 31, '2026-06-11 09:51:59.008717+03', '+30');
INSERT INTO public.reading_log VALUES (309577, 11, 1, 32, '2026-06-11 09:51:59.115386+03', '-47.8');
INSERT INTO public.reading_log VALUES (309578, 11, 1, 33, '2026-06-11 09:51:59.207184+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309579, 11, 1, 34, '2026-06-11 09:51:59.318145+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309580, 11, 1, 29, '2026-06-11 09:52:09.624572+03', 'n/a');
INSERT INTO public.reading_log VALUES (309581, 11, 1, 30, '2026-06-11 09:52:09.720782+03', 'n/a');
INSERT INTO public.reading_log VALUES (309582, 11, 1, 31, '2026-06-11 09:52:09.82816+03', '+30');
INSERT INTO public.reading_log VALUES (309583, 11, 1, 32, '2026-06-11 09:52:09.93555+03', '-47.8');
INSERT INTO public.reading_log VALUES (309584, 11, 1, 33, '2026-06-11 09:52:10.042838+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309585, 11, 1, 34, '2026-06-11 09:52:10.131157+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309586, 11, 1, 29, '2026-06-11 09:52:20.386546+03', 'n/a');
INSERT INTO public.reading_log VALUES (309587, 11, 1, 30, '2026-06-11 09:52:20.479244+03', 'n/a');
INSERT INTO public.reading_log VALUES (309588, 11, 1, 31, '2026-06-11 09:52:20.588369+03', '+31');
INSERT INTO public.reading_log VALUES (309589, 11, 1, 32, '2026-06-11 09:52:20.697426+03', '-47.8');
INSERT INTO public.reading_log VALUES (309590, 11, 1, 33, '2026-06-11 09:52:20.789038+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309591, 11, 1, 34, '2026-06-11 09:52:20.897339+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309592, 11, 1, 29, '2026-06-11 09:52:31.211556+03', 'n/a');
INSERT INTO public.reading_log VALUES (309593, 11, 1, 30, '2026-06-11 09:52:31.301702+03', 'n/a');
INSERT INTO public.reading_log VALUES (309594, 11, 1, 31, '2026-06-11 09:52:31.4077+03', '+30');
INSERT INTO public.reading_log VALUES (309595, 11, 1, 32, '2026-06-11 09:52:31.51562+03', '-47.4');
INSERT INTO public.reading_log VALUES (309596, 11, 1, 33, '2026-06-11 09:52:31.6127+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309597, 11, 1, 34, '2026-06-11 09:52:31.717363+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309598, 11, 1, 29, '2026-06-11 09:52:42.021317+03', 'n/a');
INSERT INTO public.reading_log VALUES (309599, 11, 1, 30, '2026-06-11 09:52:42.11751+03', 'n/a');
INSERT INTO public.reading_log VALUES (309600, 11, 1, 31, '2026-06-11 09:52:42.236414+03', '+30');
INSERT INTO public.reading_log VALUES (309601, 11, 1, 32, '2026-06-11 09:52:42.319832+03', '-47.3');
INSERT INTO public.reading_log VALUES (309602, 11, 1, 33, '2026-06-11 09:52:42.383005+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309603, 11, 1, 34, '2026-06-11 09:52:42.476762+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309604, 11, 1, 29, '2026-06-11 09:52:52.708437+03', 'n/a');
INSERT INTO public.reading_log VALUES (309605, 11, 1, 30, '2026-06-11 09:52:52.815489+03', 'n/a');
INSERT INTO public.reading_log VALUES (309606, 11, 1, 31, '2026-06-11 09:52:52.910758+03', '+30');
INSERT INTO public.reading_log VALUES (309607, 11, 1, 32, '2026-06-11 09:52:52.972715+03', '-47.8');
INSERT INTO public.reading_log VALUES (309608, 11, 1, 33, '2026-06-11 09:52:53.06734+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309609, 11, 1, 34, '2026-06-11 09:52:53.13581+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309610, 11, 1, 29, '2026-06-11 09:53:03.475232+03', 'n/a');
INSERT INTO public.reading_log VALUES (309611, 11, 1, 30, '2026-06-11 09:53:03.586116+03', 'n/a');
INSERT INTO public.reading_log VALUES (309612, 11, 1, 31, '2026-06-11 09:53:03.678834+03', '+30');
INSERT INTO public.reading_log VALUES (309613, 11, 1, 32, '2026-06-11 09:53:03.790389+03', '-47.7');
INSERT INTO public.reading_log VALUES (309614, 11, 1, 33, '2026-06-11 09:53:03.896464+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309615, 11, 1, 34, '2026-06-11 09:53:04.006449+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309616, 11, 1, 29, '2026-06-11 09:53:14.36331+03', 'n/a');
INSERT INTO public.reading_log VALUES (309617, 11, 1, 30, '2026-06-11 09:53:14.472807+03', 'n/a');
INSERT INTO public.reading_log VALUES (309618, 11, 1, 31, '2026-06-11 09:53:14.565144+03', '+30');
INSERT INTO public.reading_log VALUES (309619, 11, 1, 32, '2026-06-11 09:53:14.67715+03', '-47.4');
INSERT INTO public.reading_log VALUES (309620, 11, 1, 33, '2026-06-11 09:53:14.767253+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309621, 11, 1, 34, '2026-06-11 09:53:14.880969+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309622, 11, 1, 29, '2026-06-11 09:53:25.229956+03', 'n/a');
INSERT INTO public.reading_log VALUES (309623, 11, 1, 30, '2026-06-11 09:53:25.29273+03', 'n/a');
INSERT INTO public.reading_log VALUES (309624, 11, 1, 31, '2026-06-11 09:53:25.386149+03', '+30');
INSERT INTO public.reading_log VALUES (309625, 11, 1, 32, '2026-06-11 09:53:25.500163+03', '-47.2');
INSERT INTO public.reading_log VALUES (309626, 11, 1, 33, '2026-06-11 09:53:25.604742+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309627, 11, 1, 34, '2026-06-11 09:53:25.695584+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309628, 11, 1, 29, '2026-06-11 09:53:35.994868+03', 'n/a');
INSERT INTO public.reading_log VALUES (309629, 11, 1, 30, '2026-06-11 09:53:36.083159+03', 'n/a');
INSERT INTO public.reading_log VALUES (309630, 11, 1, 31, '2026-06-11 09:53:36.190758+03', '+30');
INSERT INTO public.reading_log VALUES (309631, 11, 1, 32, '2026-06-11 09:53:36.301439+03', '-47.5');
INSERT INTO public.reading_log VALUES (309632, 11, 1, 33, '2026-06-11 09:53:36.408974+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309633, 11, 1, 34, '2026-06-11 09:53:36.49988+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309634, 11, 1, 29, '2026-06-11 09:53:46.846729+03', 'n/a');
INSERT INTO public.reading_log VALUES (309635, 11, 1, 30, '2026-06-11 09:53:46.94735+03', 'n/a');
INSERT INTO public.reading_log VALUES (309636, 11, 1, 31, '2026-06-11 09:53:47.041175+03', '+30');
INSERT INTO public.reading_log VALUES (309637, 11, 1, 32, '2026-06-11 09:53:47.103304+03', '-47.4');
INSERT INTO public.reading_log VALUES (309638, 11, 1, 33, '2026-06-11 09:53:47.211425+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309639, 11, 1, 34, '2026-06-11 09:53:47.304668+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309640, 11, 1, 29, '2026-06-11 09:53:57.658804+03', 'n/a');
INSERT INTO public.reading_log VALUES (309641, 11, 1, 30, '2026-06-11 09:53:57.761979+03', 'n/a');
INSERT INTO public.reading_log VALUES (309642, 11, 1, 31, '2026-06-11 09:53:57.8562+03', '+30');
INSERT INTO public.reading_log VALUES (309643, 11, 1, 32, '2026-06-11 09:53:57.963855+03', '-47.5');
INSERT INTO public.reading_log VALUES (309644, 11, 1, 33, '2026-06-11 09:53:58.071881+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309645, 11, 1, 34, '2026-06-11 09:53:58.165942+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309646, 11, 1, 29, '2026-06-11 09:54:08.471041+03', 'n/a');
INSERT INTO public.reading_log VALUES (309647, 11, 1, 30, '2026-06-11 09:54:08.563502+03', 'n/a');
INSERT INTO public.reading_log VALUES (309648, 11, 1, 31, '2026-06-11 09:54:08.67814+03', '+30');
INSERT INTO public.reading_log VALUES (309649, 11, 1, 32, '2026-06-11 09:54:08.784292+03', '-47.6');
INSERT INTO public.reading_log VALUES (309650, 11, 1, 33, '2026-06-11 09:54:08.880324+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309651, 11, 1, 34, '2026-06-11 09:54:08.986654+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309652, 11, 1, 29, '2026-06-11 09:54:19.226347+03', 'n/a');
INSERT INTO public.reading_log VALUES (309653, 11, 1, 30, '2026-06-11 09:54:19.34197+03', 'n/a');
INSERT INTO public.reading_log VALUES (309654, 11, 1, 31, '2026-06-11 09:54:19.44047+03', '+30');
INSERT INTO public.reading_log VALUES (309655, 11, 1, 32, '2026-06-11 09:54:19.534983+03', '-47.2');
INSERT INTO public.reading_log VALUES (309656, 11, 1, 33, '2026-06-11 09:54:19.599321+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309657, 11, 1, 34, '2026-06-11 09:54:19.719817+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309658, 11, 1, 29, '2026-06-11 09:54:30.060992+03', 'n/a');
INSERT INTO public.reading_log VALUES (309659, 11, 1, 30, '2026-06-11 09:54:30.171665+03', 'n/a');
INSERT INTO public.reading_log VALUES (309660, 11, 1, 31, '2026-06-11 09:54:30.280734+03', '+30');
INSERT INTO public.reading_log VALUES (309661, 11, 1, 32, '2026-06-11 09:54:30.373913+03', '-47.7');
INSERT INTO public.reading_log VALUES (309662, 11, 1, 33, '2026-06-11 09:54:30.481949+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309663, 11, 1, 34, '2026-06-11 09:54:30.5772+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309664, 11, 1, 29, '2026-06-11 09:54:40.926707+03', 'n/a');
INSERT INTO public.reading_log VALUES (309665, 11, 1, 30, '2026-06-11 09:54:41.036477+03', 'n/a');
INSERT INTO public.reading_log VALUES (309666, 11, 1, 31, '2026-06-11 09:54:41.135299+03', '+30');
INSERT INTO public.reading_log VALUES (309667, 11, 1, 32, '2026-06-11 09:54:41.238166+03', '-47.6');
INSERT INTO public.reading_log VALUES (309668, 11, 1, 33, '2026-06-11 09:54:41.332934+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309669, 11, 1, 34, '2026-06-11 09:54:41.395766+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309670, 11, 1, 29, '2026-06-11 09:54:51.705033+03', 'n/a');
INSERT INTO public.reading_log VALUES (309671, 11, 1, 30, '2026-06-11 09:54:51.792912+03', 'n/a');
INSERT INTO public.reading_log VALUES (309672, 11, 1, 31, '2026-06-11 09:54:51.901044+03', '+30');
INSERT INTO public.reading_log VALUES (309673, 11, 1, 32, '2026-06-11 09:54:52.009213+03', '-47.6');
INSERT INTO public.reading_log VALUES (309674, 11, 1, 33, '2026-06-11 09:54:52.103122+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309675, 11, 1, 34, '2026-06-11 09:54:52.215021+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309676, 11, 1, 29, '2026-06-11 09:55:02.502354+03', 'n/a');
INSERT INTO public.reading_log VALUES (309677, 11, 1, 30, '2026-06-11 09:55:02.611129+03', 'n/a');
INSERT INTO public.reading_log VALUES (309678, 11, 1, 31, '2026-06-11 09:55:02.716605+03', '+31');
INSERT INTO public.reading_log VALUES (309679, 11, 1, 32, '2026-06-11 09:55:02.821489+03', '-47.6');
INSERT INTO public.reading_log VALUES (309680, 11, 1, 33, '2026-06-11 09:55:02.925302+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309681, 11, 1, 34, '2026-06-11 09:55:03.024298+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309682, 11, 1, 29, '2026-06-11 09:55:13.330568+03', 'n/a');
INSERT INTO public.reading_log VALUES (309683, 11, 1, 30, '2026-06-11 09:55:13.426236+03', 'n/a');
INSERT INTO public.reading_log VALUES (309684, 11, 1, 31, '2026-06-11 09:55:13.537639+03', '+30');
INSERT INTO public.reading_log VALUES (309685, 11, 1, 32, '2026-06-11 09:55:13.62861+03', '-47.4');
INSERT INTO public.reading_log VALUES (309686, 11, 1, 33, '2026-06-11 09:55:13.739475+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309687, 11, 1, 34, '2026-06-11 09:55:13.844761+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309688, 11, 1, 29, '2026-06-11 09:55:24.091738+03', 'n/a');
INSERT INTO public.reading_log VALUES (309689, 11, 1, 30, '2026-06-11 09:55:24.18307+03', 'n/a');
INSERT INTO public.reading_log VALUES (309690, 11, 1, 31, '2026-06-11 09:55:24.297055+03', '+30');
INSERT INTO public.reading_log VALUES (309691, 11, 1, 32, '2026-06-11 09:55:24.400502+03', '-47.7');
INSERT INTO public.reading_log VALUES (309692, 11, 1, 33, '2026-06-11 09:55:24.492984+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309693, 11, 1, 34, '2026-06-11 09:55:24.601484+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309694, 11, 1, 29, '2026-06-11 09:55:34.846895+03', 'n/a');
INSERT INTO public.reading_log VALUES (309695, 11, 1, 30, '2026-06-11 09:55:34.95364+03', 'n/a');
INSERT INTO public.reading_log VALUES (309696, 11, 1, 31, '2026-06-11 09:55:35.049348+03', '+30');
INSERT INTO public.reading_log VALUES (309697, 11, 1, 32, '2026-06-11 09:55:35.158548+03', '-47.9');
INSERT INTO public.reading_log VALUES (309698, 11, 1, 33, '2026-06-11 09:55:35.269299+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309699, 11, 1, 34, '2026-06-11 09:55:35.355176+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309700, 11, 1, 29, '2026-06-11 09:55:45.6141+03', 'n/a');
INSERT INTO public.reading_log VALUES (309701, 11, 1, 30, '2026-06-11 09:55:45.706174+03', 'n/a');
INSERT INTO public.reading_log VALUES (309702, 11, 1, 31, '2026-06-11 09:55:45.815154+03', '+30');
INSERT INTO public.reading_log VALUES (309703, 11, 1, 32, '2026-06-11 09:55:45.926747+03', '-48.3');
INSERT INTO public.reading_log VALUES (309704, 11, 1, 33, '2026-06-11 09:55:46.018216+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309705, 11, 1, 34, '2026-06-11 09:55:46.129312+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309706, 11, 1, 29, '2026-06-11 09:55:56.433256+03', 'n/a');
INSERT INTO public.reading_log VALUES (309707, 11, 1, 30, '2026-06-11 09:55:56.581975+03', 'n/a');
INSERT INTO public.reading_log VALUES (309708, 11, 2, 31, '2026-06-11 09:55:56.67794+03', '+30');
INSERT INTO public.reading_log VALUES (309709, 11, 1, 32, '2026-06-11 09:55:56.791699+03', '-48.7');
INSERT INTO public.reading_log VALUES (309710, 11, 1, 33, '2026-06-11 09:55:56.895126+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309711, 11, 1, 34, '2026-06-11 09:55:56.995989+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309712, 11, 1, 29, '2026-06-11 09:56:07.341548+03', 'n/a');
INSERT INTO public.reading_log VALUES (309713, 11, 1, 30, '2026-06-11 09:56:07.447529+03', 'n/a');
INSERT INTO public.reading_log VALUES (309714, 11, 2, 31, '2026-06-11 09:56:07.545259+03', '+30');
INSERT INTO public.reading_log VALUES (309715, 11, 1, 32, '2026-06-11 09:56:07.65414+03', '-48.6');
INSERT INTO public.reading_log VALUES (309716, 11, 1, 33, '2026-06-11 09:56:07.749255+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309717, 11, 1, 34, '2026-06-11 09:56:07.85813+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309718, 11, 1, 29, '2026-06-11 09:56:18.160382+03', 'n/a');
INSERT INTO public.reading_log VALUES (309719, 11, 1, 30, '2026-06-11 09:56:18.25159+03', 'n/a');
INSERT INTO public.reading_log VALUES (309720, 11, 2, 31, '2026-06-11 09:56:18.364772+03', '+31');
INSERT INTO public.reading_log VALUES (309721, 11, 1, 32, '2026-06-11 09:56:18.458108+03', '-48.5');
INSERT INTO public.reading_log VALUES (309722, 11, 1, 33, '2026-06-11 09:56:18.5655+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309723, 11, 1, 34, '2026-06-11 09:56:18.676203+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309724, 11, 1, 29, '2026-06-11 09:56:29.039984+03', 'n/a');
INSERT INTO public.reading_log VALUES (309725, 11, 1, 30, '2026-06-11 09:56:29.140606+03', 'n/a');
INSERT INTO public.reading_log VALUES (309726, 11, 3, 31, '2026-06-11 09:56:29.234088+03', '+31');
INSERT INTO public.reading_log VALUES (309727, 11, 1, 32, '2026-06-11 09:56:29.34635+03', '-48.6');
INSERT INTO public.reading_log VALUES (309728, 11, 1, 33, '2026-06-11 09:56:29.450894+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309729, 11, 1, 34, '2026-06-11 09:56:29.542613+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309730, 11, 1, 29, '2026-06-11 09:56:39.838878+03', 'n/a');
INSERT INTO public.reading_log VALUES (309731, 11, 1, 30, '2026-06-11 09:56:39.94671+03', 'n/a');
INSERT INTO public.reading_log VALUES (309732, 11, 3, 31, '2026-06-11 09:56:40.039996+03', '+30');
INSERT INTO public.reading_log VALUES (309733, 11, 1, 32, '2026-06-11 09:56:40.16041+03', '-48.4');
INSERT INTO public.reading_log VALUES (309734, 11, 1, 33, '2026-06-11 09:56:40.256007+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309735, 11, 1, 34, '2026-06-11 09:56:40.346691+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309736, 11, 1, 29, '2026-06-11 09:56:50.603546+03', 'n/a');
INSERT INTO public.reading_log VALUES (309737, 11, 1, 30, '2026-06-11 09:56:50.695173+03', 'n/a');
INSERT INTO public.reading_log VALUES (309738, 11, 1, 31, '2026-06-11 09:56:50.804838+03', '+31');
INSERT INTO public.reading_log VALUES (309739, 11, 1, 32, '2026-06-11 09:56:50.897447+03', '-48.8');
INSERT INTO public.reading_log VALUES (309740, 11, 1, 33, '2026-06-11 09:56:51.005929+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309741, 11, 1, 34, '2026-06-11 09:56:51.117023+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309742, 11, 1, 29, '2026-06-11 09:57:01.458513+03', 'n/a');
INSERT INTO public.reading_log VALUES (309743, 11, 1, 30, '2026-06-11 09:57:01.565199+03', 'n/a');
INSERT INTO public.reading_log VALUES (309744, 11, 1, 31, '2026-06-11 09:57:01.658979+03', '+30');
INSERT INTO public.reading_log VALUES (309745, 11, 1, 32, '2026-06-11 09:57:01.766143+03', '-48.8');
INSERT INTO public.reading_log VALUES (309746, 11, 1, 33, '2026-06-11 09:57:01.87548+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309747, 11, 1, 34, '2026-06-11 09:57:01.966667+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309748, 11, 1, 29, '2026-06-11 09:57:12.216657+03', 'n/a');
INSERT INTO public.reading_log VALUES (309749, 11, 1, 30, '2026-06-11 09:57:12.31037+03', 'n/a');
INSERT INTO public.reading_log VALUES (309750, 11, 1, 31, '2026-06-11 09:57:12.417827+03', '+30');
INSERT INTO public.reading_log VALUES (309751, 11, 1, 32, '2026-06-11 09:57:12.529682+03', '-49.0');
INSERT INTO public.reading_log VALUES (309752, 11, 1, 33, '2026-06-11 09:57:12.620949+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309753, 11, 1, 34, '2026-06-11 09:57:12.729804+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309754, 11, 1, 29, '2026-06-11 09:57:23.032751+03', 'n/a');
INSERT INTO public.reading_log VALUES (309755, 11, 1, 30, '2026-06-11 09:57:23.13195+03', 'n/a');
INSERT INTO public.reading_log VALUES (309756, 11, 1, 31, '2026-06-11 09:57:23.236587+03', '+30');
INSERT INTO public.reading_log VALUES (309757, 11, 1, 32, '2026-06-11 09:57:23.346511+03', '-48.9');
INSERT INTO public.reading_log VALUES (309758, 11, 1, 33, '2026-06-11 09:57:23.439045+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309759, 11, 1, 34, '2026-06-11 09:57:23.546738+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309760, 11, 1, 29, '2026-06-11 09:57:33.844832+03', 'n/a');
INSERT INTO public.reading_log VALUES (309761, 11, 1, 30, '2026-06-11 09:57:33.966248+03', 'n/a');
INSERT INTO public.reading_log VALUES (309762, 11, 1, 31, '2026-06-11 09:57:34.053421+03', '+31');
INSERT INTO public.reading_log VALUES (309763, 11, 1, 32, '2026-06-11 09:57:34.163231+03', '-49.0');
INSERT INTO public.reading_log VALUES (309764, 11, 1, 33, '2026-06-11 09:57:34.271434+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309765, 11, 1, 34, '2026-06-11 09:57:34.364989+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309766, 11, 1, 29, '2026-06-11 09:57:44.738393+03', 'n/a');
INSERT INTO public.reading_log VALUES (309767, 11, 1, 30, '2026-06-11 09:57:44.828036+03', 'n/a');
INSERT INTO public.reading_log VALUES (309768, 11, 1, 31, '2026-06-11 09:57:44.93521+03', '+30');
INSERT INTO public.reading_log VALUES (309769, 11, 1, 32, '2026-06-11 09:57:45.027463+03', '-48.9');
INSERT INTO public.reading_log VALUES (309770, 11, 1, 33, '2026-06-11 09:57:45.141351+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309771, 11, 1, 34, '2026-06-11 09:57:45.247646+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309772, 11, 1, 29, '2026-06-11 09:57:55.552122+03', 'n/a');
INSERT INTO public.reading_log VALUES (309773, 11, 1, 30, '2026-06-11 09:57:55.647889+03', 'n/a');
INSERT INTO public.reading_log VALUES (309774, 11, 1, 31, '2026-06-11 09:57:55.756443+03', '+30');
INSERT INTO public.reading_log VALUES (309775, 11, 1, 32, '2026-06-11 09:57:55.852967+03', '-48.9');
INSERT INTO public.reading_log VALUES (309776, 11, 1, 33, '2026-06-11 09:57:55.91692+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309777, 11, 1, 34, '2026-06-11 09:57:56.009867+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309778, 11, 1, 29, '2026-06-11 09:58:06.265725+03', 'n/a');
INSERT INTO public.reading_log VALUES (309779, 11, 1, 30, '2026-06-11 09:58:06.376434+03', 'n/a');
INSERT INTO public.reading_log VALUES (309780, 11, 1, 31, '2026-06-11 09:58:06.466324+03', '+30');
INSERT INTO public.reading_log VALUES (309781, 11, 1, 32, '2026-06-11 09:58:06.576596+03', '-48.9');
INSERT INTO public.reading_log VALUES (309782, 11, 1, 33, '2026-06-11 09:58:06.684947+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309783, 11, 1, 34, '2026-06-11 09:58:06.781071+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309784, 11, 1, 29, '2026-06-11 09:58:17.083716+03', 'n/a');
INSERT INTO public.reading_log VALUES (309785, 11, 1, 30, '2026-06-11 09:58:17.176597+03', 'n/a');
INSERT INTO public.reading_log VALUES (309786, 11, 1, 31, '2026-06-11 09:58:17.282701+03', '+31');
INSERT INTO public.reading_log VALUES (309787, 11, 1, 32, '2026-06-11 09:58:17.392441+03', '-49.0');
INSERT INTO public.reading_log VALUES (309788, 11, 1, 33, '2026-06-11 09:58:17.487071+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309789, 11, 1, 34, '2026-06-11 09:58:17.599416+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309790, 11, 1, 29, '2026-06-11 09:58:27.900258+03', 'n/a');
INSERT INTO public.reading_log VALUES (309791, 11, 1, 30, '2026-06-11 09:58:27.997247+03', 'n/a');
INSERT INTO public.reading_log VALUES (309792, 11, 1, 31, '2026-06-11 09:58:28.10446+03', '+30');
INSERT INTO public.reading_log VALUES (309793, 11, 1, 32, '2026-06-11 09:58:28.197649+03', '-48.8');
INSERT INTO public.reading_log VALUES (309794, 11, 1, 33, '2026-06-11 09:58:28.259541+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309795, 11, 1, 34, '2026-06-11 09:58:28.35342+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309796, 11, 1, 29, '2026-06-11 09:58:38.60521+03', 'n/a');
INSERT INTO public.reading_log VALUES (309797, 11, 1, 30, '2026-06-11 09:58:38.693055+03', 'n/a');
INSERT INTO public.reading_log VALUES (309798, 11, 1, 31, '2026-06-11 09:58:38.761805+03', '+30');
INSERT INTO public.reading_log VALUES (309799, 11, 1, 32, '2026-06-11 09:58:38.855814+03', '-48.7');
INSERT INTO public.reading_log VALUES (309800, 11, 1, 33, '2026-06-11 09:58:38.961912+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309801, 11, 1, 34, '2026-06-11 09:58:39.061057+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309802, 11, 1, 29, '2026-06-11 09:58:49.352979+03', 'n/a');
INSERT INTO public.reading_log VALUES (309803, 11, 1, 30, '2026-06-11 09:58:49.444107+03', 'n/a');
INSERT INTO public.reading_log VALUES (309804, 11, 1, 31, '2026-06-11 09:58:49.557239+03', '+31');
INSERT INTO public.reading_log VALUES (309805, 11, 1, 32, '2026-06-11 09:58:49.648124+03', '-48.9');
INSERT INTO public.reading_log VALUES (309806, 11, 1, 33, '2026-06-11 09:58:49.709954+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309807, 11, 1, 34, '2026-06-11 09:58:49.821747+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309808, 11, 1, 29, '2026-06-11 09:59:00.069102+03', 'n/a');
INSERT INTO public.reading_log VALUES (309809, 11, 1, 30, '2026-06-11 09:59:00.165583+03', 'n/a');
INSERT INTO public.reading_log VALUES (309810, 11, 1, 31, '2026-06-11 09:59:00.277097+03', '+30');
INSERT INTO public.reading_log VALUES (309811, 11, 1, 32, '2026-06-11 09:59:00.367545+03', '-48.9');
INSERT INTO public.reading_log VALUES (309812, 11, 1, 33, '2026-06-11 09:59:00.430697+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309813, 11, 1, 34, '2026-06-11 09:59:00.536507+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309814, 11, 1, 29, '2026-06-11 09:59:10.780361+03', 'n/a');
INSERT INTO public.reading_log VALUES (309815, 11, 1, 30, '2026-06-11 09:59:10.891823+03', 'n/a');
INSERT INTO public.reading_log VALUES (309816, 11, 1, 31, '2026-06-11 09:59:10.994852+03', '+30');
INSERT INTO public.reading_log VALUES (309817, 11, 1, 32, '2026-06-11 09:59:11.090017+03', '-48.9');
INSERT INTO public.reading_log VALUES (309818, 11, 1, 33, '2026-06-11 09:59:11.200592+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309819, 11, 1, 34, '2026-06-11 09:59:11.293307+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309820, 11, 1, 29, '2026-06-11 09:59:21.532504+03', 'n/a');
INSERT INTO public.reading_log VALUES (309821, 11, 1, 30, '2026-06-11 09:59:21.64632+03', 'n/a');
INSERT INTO public.reading_log VALUES (309822, 11, 1, 31, '2026-06-11 09:59:21.756423+03', '+31');
INSERT INTO public.reading_log VALUES (309823, 11, 1, 32, '2026-06-11 09:59:21.844298+03', '-48.8');
INSERT INTO public.reading_log VALUES (309824, 11, 1, 33, '2026-06-11 09:59:21.954061+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309825, 11, 1, 34, '2026-06-11 09:59:22.058924+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309826, 11, 1, 29, '2026-06-11 09:59:32.402472+03', 'n/a');
INSERT INTO public.reading_log VALUES (309827, 11, 1, 30, '2026-06-11 09:59:32.502941+03', 'n/a');
INSERT INTO public.reading_log VALUES (309828, 11, 1, 31, '2026-06-11 09:59:32.607338+03', '+31');
INSERT INTO public.reading_log VALUES (309829, 11, 1, 32, '2026-06-11 09:59:32.718974+03', '-48.9');
INSERT INTO public.reading_log VALUES (309830, 11, 1, 33, '2026-06-11 09:59:32.812213+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309831, 11, 1, 34, '2026-06-11 09:59:32.923335+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309832, 11, 1, 29, '2026-06-11 09:59:43.241435+03', 'n/a');
INSERT INTO public.reading_log VALUES (309833, 11, 1, 30, '2026-06-11 09:59:43.325887+03', 'n/a');
INSERT INTO public.reading_log VALUES (309834, 11, 1, 31, '2026-06-11 09:59:43.434727+03', '+30');
INSERT INTO public.reading_log VALUES (309835, 11, 1, 32, '2026-06-11 09:59:43.550008+03', '-48.8');
INSERT INTO public.reading_log VALUES (309836, 11, 1, 33, '2026-06-11 09:59:43.636595+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309837, 11, 1, 34, '2026-06-11 09:59:43.742552+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309838, 11, 1, 29, '2026-06-11 09:59:54.096629+03', 'n/a');
INSERT INTO public.reading_log VALUES (309839, 11, 1, 30, '2026-06-11 09:59:54.205005+03', 'n/a');
INSERT INTO public.reading_log VALUES (309840, 11, 1, 31, '2026-06-11 09:59:54.300418+03', '+31');
INSERT INTO public.reading_log VALUES (309841, 11, 1, 32, '2026-06-11 09:59:54.417591+03', '-48.5');
INSERT INTO public.reading_log VALUES (309842, 11, 1, 33, '2026-06-11 09:59:54.517253+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309843, 11, 1, 34, '2026-06-11 09:59:54.609739+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309844, 11, 1, 29, '2026-06-11 10:00:04.909317+03', 'n/a');
INSERT INTO public.reading_log VALUES (309845, 11, 1, 30, '2026-06-11 10:00:05.004333+03', 'n/a');
INSERT INTO public.reading_log VALUES (309846, 11, 1, 31, '2026-06-11 10:00:05.112373+03', '+31');
INSERT INTO public.reading_log VALUES (309847, 11, 1, 32, '2026-06-11 10:00:05.208646+03', '-48.5');
INSERT INTO public.reading_log VALUES (309848, 11, 1, 33, '2026-06-11 10:00:05.317039+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309849, 11, 1, 34, '2026-06-11 10:00:05.428022+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309850, 11, 1, 29, '2026-06-11 10:00:15.746733+03', 'n/a');
INSERT INTO public.reading_log VALUES (309851, 11, 1, 30, '2026-06-11 10:00:15.847212+03', 'n/a');
INSERT INTO public.reading_log VALUES (309852, 11, 1, 31, '2026-06-11 10:00:15.941884+03', '+30');
INSERT INTO public.reading_log VALUES (309853, 11, 1, 32, '2026-06-11 10:00:16.049839+03', '-48.0');
INSERT INTO public.reading_log VALUES (309854, 11, 1, 33, '2026-06-11 10:00:16.143408+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309855, 11, 1, 34, '2026-06-11 10:00:16.251394+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309856, 11, 1, 29, '2026-06-11 10:00:26.556098+03', 'n/a');
INSERT INTO public.reading_log VALUES (309857, 11, 1, 30, '2026-06-11 10:00:26.647038+03', 'n/a');
INSERT INTO public.reading_log VALUES (309858, 11, 1, 31, '2026-06-11 10:00:26.711052+03', '+30');
INSERT INTO public.reading_log VALUES (309859, 11, 1, 32, '2026-06-11 10:00:26.817845+03', '-48.3');
INSERT INTO public.reading_log VALUES (309860, 11, 1, 33, '2026-06-11 10:00:26.912409+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309861, 11, 1, 34, '2026-06-11 10:00:27.021766+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309862, 11, 1, 29, '2026-06-11 10:00:37.359712+03', 'n/a');
INSERT INTO public.reading_log VALUES (309863, 11, 1, 30, '2026-06-11 10:00:37.469444+03', 'n/a');
INSERT INTO public.reading_log VALUES (309864, 11, 1, 31, '2026-06-11 10:00:37.579889+03', '+31');
INSERT INTO public.reading_log VALUES (309865, 11, 1, 32, '2026-06-11 10:00:37.675623+03', '-48.5');
INSERT INTO public.reading_log VALUES (309866, 11, 1, 33, '2026-06-11 10:00:37.863175+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309867, 11, 1, 34, '2026-06-11 10:00:37.981367+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309868, 11, 1, 29, '2026-06-11 10:00:48.331572+03', 'n/a');
INSERT INTO public.reading_log VALUES (309869, 11, 1, 30, '2026-06-11 10:00:48.448913+03', 'n/a');
INSERT INTO public.reading_log VALUES (309870, 11, 1, 31, '2026-06-11 10:00:48.538187+03', '+31');
INSERT INTO public.reading_log VALUES (309871, 11, 1, 32, '2026-06-11 10:00:48.649257+03', '-48.7');
INSERT INTO public.reading_log VALUES (309872, 11, 1, 33, '2026-06-11 10:00:48.742201+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309873, 11, 1, 34, '2026-06-11 10:00:48.850688+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309874, 11, 1, 29, '2026-06-11 10:00:59.08535+03', 'n/a');
INSERT INTO public.reading_log VALUES (309875, 11, 1, 30, '2026-06-11 10:00:59.191928+03', 'n/a');
INSERT INTO public.reading_log VALUES (309876, 11, 1, 31, '2026-06-11 10:00:59.298674+03', '+30');
INSERT INTO public.reading_log VALUES (309877, 11, 1, 32, '2026-06-11 10:00:59.396542+03', '-48.4');
INSERT INTO public.reading_log VALUES (309878, 11, 1, 33, '2026-06-11 10:00:59.50862+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309879, 11, 1, 34, '2026-06-11 10:00:59.612752+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309880, 11, 1, 29, '2026-06-11 10:01:09.917882+03', 'n/a');
INSERT INTO public.reading_log VALUES (309881, 11, 1, 30, '2026-06-11 10:01:10.007073+03', 'n/a');
INSERT INTO public.reading_log VALUES (309882, 11, 1, 31, '2026-06-11 10:01:10.123662+03', '+31');
INSERT INTO public.reading_log VALUES (309883, 11, 1, 32, '2026-06-11 10:01:10.223546+03', '-48.7');
INSERT INTO public.reading_log VALUES (309884, 11, 1, 33, '2026-06-11 10:01:10.320229+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309885, 11, 1, 34, '2026-06-11 10:01:10.425876+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309886, 11, 1, 29, '2026-06-11 10:01:20.722741+03', 'n/a');
INSERT INTO public.reading_log VALUES (309887, 11, 1, 30, '2026-06-11 10:01:20.830815+03', 'n/a');
INSERT INTO public.reading_log VALUES (309888, 11, 1, 31, '2026-06-11 10:01:20.927359+03', '+30');
INSERT INTO public.reading_log VALUES (309889, 11, 1, 32, '2026-06-11 10:01:21.036744+03', '-48.5');
INSERT INTO public.reading_log VALUES (309890, 11, 1, 33, '2026-06-11 10:01:21.123745+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309891, 11, 1, 34, '2026-06-11 10:01:21.234957+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309892, 11, 1, 29, '2026-06-11 10:01:31.541824+03', 'n/a');
INSERT INTO public.reading_log VALUES (309893, 11, 1, 30, '2026-06-11 10:01:31.648782+03', 'n/a');
INSERT INTO public.reading_log VALUES (309894, 11, 1, 31, '2026-06-11 10:01:31.756393+03', '+31');
INSERT INTO public.reading_log VALUES (309895, 11, 1, 32, '2026-06-11 10:01:31.848415+03', '-48.7');
INSERT INTO public.reading_log VALUES (309896, 11, 1, 33, '2026-06-11 10:01:31.96088+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309897, 11, 1, 34, '2026-06-11 10:01:32.071601+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309898, 11, 1, 29, '2026-06-11 10:01:42.42321+03', 'n/a');
INSERT INTO public.reading_log VALUES (309899, 11, 1, 30, '2026-06-11 10:01:42.532574+03', 'n/a');
INSERT INTO public.reading_log VALUES (309900, 11, 1, 31, '2026-06-11 10:01:42.626524+03', '+31');
INSERT INTO public.reading_log VALUES (309901, 11, 1, 32, '2026-06-11 10:01:42.735913+03', '-48.4');
INSERT INTO public.reading_log VALUES (309902, 11, 1, 33, '2026-06-11 10:01:42.84596+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309903, 11, 1, 34, '2026-06-11 10:01:42.940113+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309904, 11, 1, 29, '2026-06-11 10:01:53.241222+03', 'n/a');
INSERT INTO public.reading_log VALUES (309905, 11, 1, 30, '2026-06-11 10:01:53.346983+03', 'n/a');
INSERT INTO public.reading_log VALUES (309906, 11, 1, 31, '2026-06-11 10:01:53.458297+03', '+31');
INSERT INTO public.reading_log VALUES (309907, 11, 1, 32, '2026-06-11 10:01:53.551096+03', '-48.6');
INSERT INTO public.reading_log VALUES (309908, 11, 1, 33, '2026-06-11 10:01:53.660758+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309909, 11, 1, 34, '2026-06-11 10:01:53.769775+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309910, 11, 1, 29, '2026-06-11 10:02:04.053586+03', 'n/a');
INSERT INTO public.reading_log VALUES (309911, 11, 1, 30, '2026-06-11 10:02:04.164297+03', 'n/a');
INSERT INTO public.reading_log VALUES (309912, 11, 1, 31, '2026-06-11 10:02:04.272732+03', '+31');
INSERT INTO public.reading_log VALUES (309913, 11, 1, 32, '2026-06-11 10:02:04.365244+03', '-48.5');
INSERT INTO public.reading_log VALUES (309914, 11, 1, 33, '2026-06-11 10:02:04.472257+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309915, 11, 1, 34, '2026-06-11 10:02:04.581961+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309916, 11, 1, 29, '2026-06-11 10:02:14.929687+03', 'n/a');
INSERT INTO public.reading_log VALUES (309917, 11, 1, 30, '2026-06-11 10:02:15.039485+03', 'n/a');
INSERT INTO public.reading_log VALUES (309918, 11, 1, 31, '2026-06-11 10:02:15.128662+03', '+30');
INSERT INTO public.reading_log VALUES (309919, 11, 1, 32, '2026-06-11 10:02:15.239117+03', '-48.6');
INSERT INTO public.reading_log VALUES (309920, 11, 1, 33, '2026-06-11 10:02:15.348431+03', 'OFF due to front...');
INSERT INTO public.reading_log VALUES (309921, 11, 1, 34, '2026-06-11 10:02:15.441766+03', 'HDB3');
INSERT INTO public.reading_log VALUES (309922, 11, 2, 31, '2026-11-09 17:40:46.92337+03', '34');
INSERT INTO public.reading_log VALUES (309923, 11, 2, 31, '2026-11-10 17:52:57.267233+03', '35');
INSERT INTO public.reading_log VALUES (309924, 11, 2, 31, '2026-11-10 18:15:37.659357+03', '36');
INSERT INTO public.reading_log VALUES (309925, 11, 2, 31, '2026-11-10 18:17:10.653637+03', '36');
INSERT INTO public.reading_log VALUES (309926, 11, 3, 32, '2026-11-12 21:21:48.608118+03', '-1');
INSERT INTO public.reading_log VALUES (309927, 11, 3, 32, '2026-11-12 21:21:53.249013+03', '-1');
INSERT INTO public.reading_log VALUES (309928, 11, 2, 31, '2026-11-12 21:22:04.38192+03', '37');
INSERT INTO public.reading_log VALUES (309929, 11, 2, 31, '2026-11-12 21:22:07.877406+03', '37');
INSERT INTO public.reading_log VALUES (309930, 11, 2, 31, '2026-11-12 21:22:11.362449+03', '37');
INSERT INTO public.reading_log VALUES (309931, 12, 2, 31, '2026-11-12 21:22:45.242695+03', '37');
INSERT INTO public.reading_log VALUES (309932, 11, 2, 31, '2026-11-12 21:54:50.573013+03', '+37');
INSERT INTO public.reading_log VALUES (309933, 11, 1, 31, '2026-11-12 21:55:36.571896+03', '+30');
INSERT INTO public.reading_log VALUES (309934, 11, 2, 31, '2026-11-12 22:10:37.323391+03', '+35');
INSERT INTO public.reading_log VALUES (309935, 12, 2, 31, '2026-11-12 22:10:43.146453+03', '+35');
INSERT INTO public.reading_log VALUES (309936, 13, 2, 31, '2026-11-12 22:10:49.630747+03', '+35');
INSERT INTO public.reading_log VALUES (309937, 14, 2, 31, '2026-11-12 22:10:55.153857+03', '+35');
INSERT INTO public.reading_log VALUES (309938, 15, 2, 31, '2026-11-12 22:11:01.771149+03', '+35');
INSERT INTO public.reading_log VALUES (309939, 16, 2, 31, '2026-11-12 22:11:06.720834+03', '+35');
INSERT INTO public.reading_log VALUES (309940, 17, 2, 31, '2026-11-12 22:11:12.601673+03', '+35');
INSERT INTO public.reading_log VALUES (309941, 18, 2, 31, '2026-11-12 22:11:17.543484+03', '+35');
INSERT INTO public.reading_log VALUES (309943, 11, 2, 31, '2026-11-12 22:11:33.567336+03', '+35');


--
-- Data for Name: reading_type; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.reading_type VALUES (29, 'ber', 'ber', NULL, NULL, NULL);
INSERT INTO public.reading_type VALUES (30, 'ebn', 'ebn', NULL, NULL, NULL);
INSERT INTO public.reading_type VALUES (33, 'txo', 'txo', NULL, NULL, NULL);
INSERT INTO public.reading_type VALUES (34, 'ttc', 'ttc', NULL, NULL, NULL);
INSERT INTO public.reading_type VALUES (31, 'tmp', 'Температура', '°C', 27.00, 33.00);
INSERT INTO public.reading_type VALUES (32, 'rsl', 'rsl', NULL, 0.00, 1.00);
INSERT INTO public.reading_type VALUES (35, 'new_param', 'new_param', NULL, NULL, NULL);


--
-- Data for Name: role; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.role VALUES (2, 'Инженер');
INSERT INTO public.role VALUES (1, 'Администратор');


--
-- Data for Name: session; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: status; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.status VALUES (1, 'ok');
INSERT INTO public.status VALUES (2, 'warning');
INSERT INTO public.status VALUES (3, 'error');


--
-- Data for Name: user; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public."user" VALUES (4, 'admin', '$2b$12$O1iqmrfuMgMrlRtGvUgggeM1X5EJzJKmDj/qydpMUiP7UVZ.gkmpu', 'admin', 'admin', 'admin');
INSERT INTO public."user" VALUES (28, 'https', '$2a$11$BJGsma68YcSODoqE8hpn/eLlOiKKu3CQuLjexcw.o.EKiuY9z8bHy', '123', '123', '123');


--
-- Data for Name: user_alert; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.user_alert VALUES (2, 1, 28, false, NULL);
INSERT INTO public.user_alert VALUES (1, 1, 4, true, '2026-11-10 17:53:24.62183+03');
INSERT INTO public.user_alert VALUES (4, 2, 28, false, NULL);
INSERT INTO public.user_alert VALUES (6, 3, 28, false, NULL);
INSERT INTO public.user_alert VALUES (8, 4, 28, false, NULL);
INSERT INTO public.user_alert VALUES (10, 5, 28, false, NULL);
INSERT INTO public.user_alert VALUES (12, 6, 28, false, NULL);
INSERT INTO public.user_alert VALUES (14, 7, 28, false, NULL);
INSERT INTO public.user_alert VALUES (3, 2, 4, true, '2026-11-10 18:15:04.831493+03');
INSERT INTO public.user_alert VALUES (5, 3, 4, true, '2026-11-10 18:15:05.045831+03');
INSERT INTO public.user_alert VALUES (7, 4, 4, true, '2026-11-10 18:15:05.103871+03');
INSERT INTO public.user_alert VALUES (9, 5, 4, true, '2026-11-10 18:15:05.147223+03');
INSERT INTO public.user_alert VALUES (11, 6, 4, true, '2026-11-10 18:15:05.189113+03');
INSERT INTO public.user_alert VALUES (13, 7, 4, true, '2026-11-10 18:15:05.240117+03');
INSERT INTO public.user_alert VALUES (16, 8, 28, false, NULL);
INSERT INTO public.user_alert VALUES (15, 8, 4, true, '2026-11-10 18:16:45.375432+03');
INSERT INTO public.user_alert VALUES (18, 9, 28, false, NULL);
INSERT INTO public.user_alert VALUES (17, 9, 4, true, '2026-11-12 21:20:27.521821+03');
INSERT INTO public.user_alert VALUES (19, 10, 4, false, NULL);
INSERT INTO public.user_alert VALUES (20, 10, 28, false, NULL);
INSERT INTO public.user_alert VALUES (21, 11, 4, false, NULL);
INSERT INTO public.user_alert VALUES (22, 11, 28, false, NULL);
INSERT INTO public.user_alert VALUES (23, 12, 4, false, NULL);
INSERT INTO public.user_alert VALUES (24, 12, 28, false, NULL);
INSERT INTO public.user_alert VALUES (25, 13, 4, false, NULL);
INSERT INTO public.user_alert VALUES (26, 13, 28, false, NULL);
INSERT INTO public.user_alert VALUES (28, 14, 28, false, NULL);
INSERT INTO public.user_alert VALUES (30, 15, 28, false, NULL);
INSERT INTO public.user_alert VALUES (29, 15, 4, true, '2026-11-12 21:22:54.251543+03');
INSERT INTO public.user_alert VALUES (27, 14, 4, true, '2026-11-12 21:22:54.278131+03');
INSERT INTO public.user_alert VALUES (32, 16, 28, false, NULL);
INSERT INTO public.user_alert VALUES (31, 16, 4, true, '2026-11-12 21:55:38.464749+03');
INSERT INTO public.user_alert VALUES (33, 17, 4, false, NULL);
INSERT INTO public.user_alert VALUES (34, 17, 28, false, NULL);
INSERT INTO public.user_alert VALUES (36, 18, 28, false, NULL);
INSERT INTO public.user_alert VALUES (38, 19, 28, false, NULL);
INSERT INTO public.user_alert VALUES (40, 20, 28, false, NULL);
INSERT INTO public.user_alert VALUES (42, 21, 28, false, NULL);
INSERT INTO public.user_alert VALUES (44, 22, 28, false, NULL);
INSERT INTO public.user_alert VALUES (46, 23, 28, false, NULL);
INSERT INTO public.user_alert VALUES (48, 24, 28, false, NULL);
INSERT INTO public.user_alert VALUES (50, 25, 28, false, NULL);
INSERT INTO public.user_alert VALUES (47, 24, 4, true, '2026-11-12 22:11:35.982883+03');
INSERT INTO public.user_alert VALUES (41, 21, 4, true, '2026-11-12 22:11:36.003738+03');
INSERT INTO public.user_alert VALUES (39, 20, 4, true, '2026-11-12 22:11:36.020788+03');
INSERT INTO public.user_alert VALUES (37, 19, 4, true, '2026-11-12 22:11:36.039924+03');
INSERT INTO public.user_alert VALUES (35, 18, 4, true, '2026-11-12 22:11:36.062609+03');
INSERT INTO public.user_alert VALUES (49, 25, 4, true, '2026-11-12 22:12:44.871914+03');
INSERT INTO public.user_alert VALUES (45, 23, 4, true, '2026-11-12 22:12:44.888492+03');
INSERT INTO public.user_alert VALUES (43, 22, 4, true, '2026-11-12 22:12:44.903735+03');


--
-- Data for Name: user_role; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.user_role VALUES (4, 1);
INSERT INTO public.user_role VALUES (28, 2);


--
-- Name: admin_audit_log_audit_log_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.admin_audit_log_audit_log_id_seq', 33, true);


--
-- Name: alert_alert_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.alert_alert_id_seq', 25, true);


--
-- Name: equipment_command_command_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.equipment_command_command_id_seq', 8, true);


--
-- Name: equipment_equipment_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.equipment_equipment_id_seq', 22, true);


--
-- Name: reading_log_reading_log_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.reading_log_reading_log_id_seq', 309943, true);


--
-- Name: reading_type_reading_type_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.reading_type_reading_type_id_seq', 35, true);


--
-- Name: role_role_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.role_role_id_seq', 2, true);


--
-- Name: session_session_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.session_session_id_seq', 180, true);


--
-- Name: status_status_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.status_status_id_seq', 3, true);


--
-- Name: user_alert_user_alert_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.user_alert_user_alert_id_seq', 50, true);


--
-- Name: user_user_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.user_user_id_seq', 28, true);


--
-- Name: admin_audit_log admin_audit_log_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_audit_log
    ADD CONSTRAINT admin_audit_log_pkey PRIMARY KEY (audit_log_id);


--
-- Name: alert alert_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.alert
    ADD CONSTRAINT alert_pkey PRIMARY KEY (alert_id);


--
-- Name: equipment_command equipment_command_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.equipment_command
    ADD CONSTRAINT equipment_command_pkey PRIMARY KEY (command_id);


--
-- Name: equipment equipment_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.equipment
    ADD CONSTRAINT equipment_pkey PRIMARY KEY (equipment_id);


--
-- Name: equipment equipment_serial_number_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.equipment
    ADD CONSTRAINT equipment_serial_number_key UNIQUE (serial_number);


--
-- Name: reading_log reading_log_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reading_log
    ADD CONSTRAINT reading_log_pkey PRIMARY KEY (reading_log_id);


--
-- Name: reading_type reading_type_identifier_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reading_type
    ADD CONSTRAINT reading_type_identifier_key UNIQUE (identifier);


--
-- Name: reading_type reading_type_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reading_type
    ADD CONSTRAINT reading_type_pkey PRIMARY KEY (reading_type_id);


--
-- Name: role role_name_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role
    ADD CONSTRAINT role_name_key UNIQUE (name);


--
-- Name: role role_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role
    ADD CONSTRAINT role_pkey PRIMARY KEY (role_id);


--
-- Name: session session_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.session
    ADD CONSTRAINT session_pkey PRIMARY KEY (session_id);


--
-- Name: session session_refresh_token_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.session
    ADD CONSTRAINT session_refresh_token_key UNIQUE (refresh_token);


--
-- Name: status status_name_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.status
    ADD CONSTRAINT status_name_key UNIQUE (name);


--
-- Name: status status_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.status
    ADD CONSTRAINT status_pkey PRIMARY KEY (status_id);


--
-- Name: user_alert uq_alert_user; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_alert
    ADD CONSTRAINT uq_alert_user UNIQUE (alert_id, user_id);


--
-- Name: equipment_command uq_equipment_cmd; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.equipment_command
    ADD CONSTRAINT uq_equipment_cmd UNIQUE (equipment_id, command_string);


--
-- Name: user_alert user_alert_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_alert
    ADD CONSTRAINT user_alert_pkey PRIMARY KEY (user_alert_id);


--
-- Name: user user_login_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."user"
    ADD CONSTRAINT user_login_key UNIQUE (login);


--
-- Name: user user_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."user"
    ADD CONSTRAINT user_pkey PRIMARY KEY (user_id);


--
-- Name: user_role user_role_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_role
    ADD CONSTRAINT user_role_pkey PRIMARY KEY (user_id, role_id);


--
-- Name: idx_alert_equipment_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_alert_equipment_active ON public.alert USING btree (equipment_id) WHERE (is_resolved = false);


--
-- Name: idx_audit_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_audit_created_at ON public.admin_audit_log USING btree (created_at DESC);


--
-- Name: idx_audit_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_audit_user_id ON public.admin_audit_log USING btree (user_id);


--
-- Name: idx_metrics_device_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_metrics_device_time ON public.reading_log USING btree (equipment_id, write_date_time DESC);


--
-- Name: idx_sessions_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_sessions_user ON public.session USING btree (user_id);


--
-- Name: idx_user_alert_unread; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_user_alert_unread ON public.user_alert USING btree (user_id) WHERE (is_read = false);


--
-- Name: reading_log trg_bi_reading_log_status; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_bi_reading_log_status BEFORE INSERT ON public.reading_log FOR EACH ROW EXECUTE FUNCTION public.check_reading_status();


--
-- Name: session trg_clean_expired_sessions; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_clean_expired_sessions BEFORE INSERT ON public.session FOR EACH ROW EXECUTE FUNCTION public.clean_expired_sessions();


--
-- Name: reading_log trg_clean_old_logs; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_clean_old_logs AFTER INSERT ON public.reading_log FOR EACH STATEMENT EXECUTE FUNCTION public.clean_old_reading_logs();


--
-- Name: equipment trg_equipment_status_create_alert; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_equipment_status_create_alert AFTER UPDATE OF status_id ON public.equipment FOR EACH ROW EXECUTE FUNCTION public.fn_create_alert_on_equipment_status();


--
-- Name: reading_log trg_reading_log_create_alert; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_reading_log_create_alert AFTER INSERT ON public.reading_log FOR EACH ROW EXECUTE FUNCTION public.fn_create_alert_on_reading_log();


--
-- Name: equipment equipment_status_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.equipment
    ADD CONSTRAINT equipment_status_id_fkey FOREIGN KEY (status_id) REFERENCES public.status(status_id) ON DELETE RESTRICT;


--
-- Name: alert fk_alert_equipment; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.alert
    ADD CONSTRAINT fk_alert_equipment FOREIGN KEY (equipment_id) REFERENCES public.equipment(equipment_id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: alert fk_alert_reading_log; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.alert
    ADD CONSTRAINT fk_alert_reading_log FOREIGN KEY (reading_log_id) REFERENCES public.reading_log(reading_log_id) ON UPDATE CASCADE ON DELETE SET NULL;


--
-- Name: admin_audit_log fk_audit_user; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_audit_log
    ADD CONSTRAINT fk_audit_user FOREIGN KEY (user_id) REFERENCES public."user"(user_id) ON UPDATE CASCADE ON DELETE SET NULL;


--
-- Name: equipment_command fk_cmd_equipment; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.equipment_command
    ADD CONSTRAINT fk_cmd_equipment FOREIGN KEY (equipment_id) REFERENCES public.equipment(equipment_id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: equipment_command fk_cmd_reading_type; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.equipment_command
    ADD CONSTRAINT fk_cmd_reading_type FOREIGN KEY (reading_type_id) REFERENCES public.reading_type(reading_type_id) ON UPDATE CASCADE ON DELETE RESTRICT;


--
-- Name: user_alert fk_user_alert_alert; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_alert
    ADD CONSTRAINT fk_user_alert_alert FOREIGN KEY (alert_id) REFERENCES public.alert(alert_id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: user_alert fk_user_alert_user; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_alert
    ADD CONSTRAINT fk_user_alert_user FOREIGN KEY (user_id) REFERENCES public."user"(user_id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: reading_log reading_log_equipment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reading_log
    ADD CONSTRAINT reading_log_equipment_id_fkey FOREIGN KEY (equipment_id) REFERENCES public.equipment(equipment_id) ON DELETE CASCADE;


--
-- Name: reading_log reading_log_reading_type_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reading_log
    ADD CONSTRAINT reading_log_reading_type_id_fkey FOREIGN KEY (reading_type_id) REFERENCES public.reading_type(reading_type_id) ON DELETE RESTRICT;


--
-- Name: reading_log reading_log_status_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reading_log
    ADD CONSTRAINT reading_log_status_id_fkey FOREIGN KEY (status_id) REFERENCES public.status(status_id) ON DELETE RESTRICT;


--
-- Name: session session_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.session
    ADD CONSTRAINT session_user_id_fkey FOREIGN KEY (user_id) REFERENCES public."user"(user_id) ON DELETE CASCADE;


--
-- Name: user_role user_role_role_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_role
    ADD CONSTRAINT user_role_role_id_fkey FOREIGN KEY (role_id) REFERENCES public.role(role_id) ON DELETE CASCADE;


--
-- Name: user_role user_role_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_role
    ADD CONSTRAINT user_role_user_id_fkey FOREIGN KEY (user_id) REFERENCES public."user"(user_id) ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict 0KLv8DuEYWnddpCYxdknQXvA61u61J2dINqCzzC5s0shB1QQFoMFd6KrOrgsfll

